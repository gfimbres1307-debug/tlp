using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;

namespace TLP.Games.SystemCrash;

public class CrashPlayer
{
    public string Key = string.Empty;
    public string Name = string.Empty;
    public string World = string.Empty;
    public int CrashedRound;
    public int LastRoll;
    public string How = string.Empty;
}

/// <summary>
/// Elimination /random game. Round 1: everyone who rolls is in. Each round the lowest roll crashes out
/// (crashed players' rolls are ignored from then on). When two remain, the final round is highest-roll-wins.
/// </summary>
public class SystemCrashGame : IGame
{
    private readonly Plugin plugin;
    private SystemCrashConfig Config => plugin.Config.SystemCrash;
    public string Name => "System Crash";

    private enum Phase { Idle, Countdown, Rolling, Closed, Done }
    private readonly Countdown countdown;
    private enum Tie { None, Lowest, Final }

    private Phase phase = Phase.Idle;
    private int round;
    private bool finalRound;
    private Tie tie = Tie.None;
    private HashSet<string> tieSet = new();
    private DateTime openedAt;
    private int periodSeconds;
    private string prize = string.Empty;
    private int startingPlayers;

    private readonly Dictionary<string, CrashPlayer> alive = new();
    private readonly List<CrashPlayer> crashed = new();
    private readonly Dictionary<string, CrashPlayer> entrants = new();   // round-1 sign-ups
    private readonly Dictionary<string, int> rolls = new();               // this period
    private readonly List<string> pendingCrashText = new();
    private int ignoredCrashed;
    private int ignoredOther;
    private CrashPlayer? winner;
    private string lastSent = string.Empty;

    private static readonly Vector4 Green = new(0.45f, 1f, 0.55f, 1f);
    private static readonly Vector4 Gold = new(1f, 0.82f, 0.3f, 1f);
    private static readonly Vector4 Grey = new(0.65f, 0.65f, 0.7f, 1f);
    private static readonly Vector4 Pink = new(1f, 0.4f, 0.8f, 1f);
    private static readonly Vector4 Red = new(1f, 0.35f, 0.35f, 1f);

    public SystemCrashGame(Plugin plugin)
    {
        this.plugin = plugin;
        countdown = new Countdown(plugin);
    }
    public void Dispose() { }

    // ======================= chat =======================
    public void OnChatMessage(XivChatType type, SeString sender, SeString message)
    {
        if (phase != Phase.Rolling) return;
        if (!Rolls.TryParse(type, sender, message, out var r)) return;
        if (Config.OnlyStandardRolls && r.CustomRange) { ignoredOther++; return; }

        if (crashed.Any(c => c.Key == r.Key)) { ignoredCrashed++; return; }   // crashed players can't come back
        if (rolls.ContainsKey(r.Key)) return;                                    // first roll per period only

        if (round == 1 && tie == Tie.None)
        {
            if (!entrants.ContainsKey(r.Key)) entrants[r.Key] = new CrashPlayer { Key = r.Key, Name = r.Name, World = r.World };
        }
        else
        {
            if (!alive.ContainsKey(r.Key)) { ignoredOther++; return; }          // not in the game
            if (tie != Tie.None && !tieSet.Contains(r.Key)) { ignoredOther++; return; }
        }
        rolls[r.Key] = r.Value;
    }

    // ======================= timing =======================
    public void Update()
    {
        countdown.Update();
        if (phase != Phase.Rolling) return;
        if ((DateTime.Now - openedAt).TotalSeconds < periodSeconds) return;
        if (Config.AutoAdvance) Resolve();
        else phase = Phase.Closed;
    }

    private int SecondsLeft => Math.Max(0, periodSeconds - (int)(DateTime.Now - openedAt).TotalSeconds);

    private void OpenPeriod(int seconds)
    {
        rolls.Clear();
        openedAt = DateTime.Now;
        periodSeconds = Math.Max(10, seconds);
        phase = Phase.Countdown;
        countdown.Start(plugin.Config.CountdownGoRoll, () => { openedAt = DateTime.Now; phase = Phase.Rolling; });
    }

    // ======================= game flow =======================
    private void Start()
    {
        prize = Config.Prize.Trim();
        alive.Clear(); crashed.Clear(); entrants.Clear(); pendingCrashText.Clear();
        round = 1; finalRound = false; tie = Tie.None; tieSet.Clear(); winner = null;
        ignoredCrashed = ignoredOther = 0;
        OpenPeriod(Config.RoundSeconds);
        Say(Config.StartTemplate.Replace("{prize}", PrizeOrDefault).Replace("{secs}", Config.RoundSeconds.ToString()));
    }

    private string PrizeOrDefault => prize.Length > 0 ? prize : "bragging rights";

    /// <summary>End the current roll period and work out who crashes.</summary>
    private void Resolve()
    {
        if (phase != Phase.Rolling && phase != Phase.Closed) return;

        if (tie != Tie.None) { ResolveTie(); return; }

        if (round == 1)
        {
            foreach (var e in entrants.Values) alive[e.Key] = e;
            startingPlayers = alive.Count;
            if (alive.Count < 2) { Say(Config.TooFewTemplate); phase = Phase.Idle; return; }
        }

        foreach (var (k, v) in rolls) if (alive.TryGetValue(k, out var p)) p.LastRoll = v;
        var rolled = alive.Values.Where(p => rolls.ContainsKey(p.Key)).ToList();
        var noRoll = alive.Values.Where(p => !rolls.ContainsKey(p.Key)).ToList();

        if (rolled.Count == 0)
        {
            // nobody rolled: run the round again instead of crashing everyone
            OpenPeriod(Config.RoundSeconds);
            Say($"Nobody rolled! Round {round} again -- /random now, {Config.RoundSeconds} seconds!");
            return;
        }

        if (Config.NoRollIsEliminated && noRoll.Count > 0)
        {
            foreach (var p in noRoll) Crash(p, "no roll");
            pendingCrashText.Add(Config.NoRollText.Replace("{names}", JoinNames(noRoll.Select(p => p.Name).ToList())));
        }

        if (finalRound)
        {
            var top = rolled.Max(p => p.LastRoll);
            var best = rolled.Where(p => p.LastRoll == top).ToList();
            if (best.Count > 1) { StartTie(Tie.Final, best); return; }
            Win(best[0]);
            return;
        }

        if (alive.Count == 1) { Win(alive.Values.First()); return; }

        var low = rolled.Min(p => p.LastRoll);
        var lowest = rolled.Where(p => p.LastRoll == low).ToList();
        if (lowest.Count > 1)
        {
            // everyone left is tied for lowest -> treat as a tie-break of the whole group
            StartTie(Tie.Lowest, lowest);
            return;
        }
        Crash(lowest[0], $"rolled {lowest[0].LastRoll}");
        pendingCrashText.Add(CrashText(lowest[0]));
        NextRoundOrWin();
    }

    private void ResolveTie()
    {
        var group = alive.Values.Where(p => tieSet.Contains(p.Key)).ToList();
        foreach (var p in group) if (rolls.TryGetValue(p.Key, out var v)) p.LastRoll = v;
        var rolled = group.Where(p => rolls.ContainsKey(p.Key)).ToList();
        var missing = group.Where(p => !rolls.ContainsKey(p.Key)).ToList();

        if (tie == Tie.Final)
        {
            if (rolled.Count == 0) { StartTie(Tie.Final, group); return; }
            var top = rolled.Max(p => p.LastRoll);
            var best = rolled.Where(p => p.LastRoll == top).ToList();
            if (best.Count > 1) { StartTie(Tie.Final, best); return; }
            tie = Tie.None;
            Win(best[0]);
            return;
        }

        // Tie for lowest: whoever didn't re-roll crashes first; otherwise the lowest re-roll crashes.
        if (missing.Count > 0 && rolled.Count > 0)
        {
            foreach (var p in missing) Crash(p, "no tie-break roll");
            pendingCrashText.Add(Config.NoRollText.Replace("{names}", JoinNames(missing.Select(p => p.Name).ToList())));
        }
        else if (rolled.Count == 0) { StartTie(Tie.Lowest, group); return; }
        else
        {
            var low = rolled.Min(p => p.LastRoll);
            var lowest = rolled.Where(p => p.LastRoll == low).ToList();
            if (lowest.Count > 1) { StartTie(Tie.Lowest, lowest); return; }
            Crash(lowest[0], $"rolled {lowest[0].LastRoll} in tie-break");
            pendingCrashText.Add(CrashText(lowest[0]));
        }
        tie = Tie.None; tieSet.Clear();
        NextRoundOrWin();
    }

    private void StartTie(Tie kind, List<CrashPlayer> group)
    {
        tie = kind;
        tieSet = new HashSet<string>(group.Select(p => p.Key));
        var lead = pendingCrashText.Count > 0 ? string.Join(" ", pendingCrashText) + " " : string.Empty;
        pendingCrashText.Clear();
        OpenPeriod(Config.TieBreakSeconds);
        Say(lead + Config.TieTemplate.Replace("{names}", JoinNames(group.Select(p => p.Name).ToList())).Replace("{secs}", Config.TieBreakSeconds.ToString()));
    }

    private void NextRoundOrWin()
    {
        if (alive.Count == 1) { Win(alive.Values.First()); return; }
        if (alive.Count == 0) { Say(Config.TooFewTemplate); phase = Phase.Idle; return; }

        var finishedRound = round;
        round++;
        var crashedText = string.Join(" ", pendingCrashText);
        pendingCrashText.Clear();
        OpenPeriod(Config.RoundSeconds);

        if (alive.Count == 2)
        {
            finalRound = true;
            var two = alive.Values.ToList();
            Say(Config.FinalTemplate
                .Replace("{round}", finishedRound.ToString()).Replace("{crashed}", crashedText)
                .Replace("{a}", two[0].Name).Replace("{b}", two[1].Name)
                .Replace("{secs}", Config.RoundSeconds.ToString()));
        }
        else
        {
            Say(Config.RoundTemplate
                .Replace("{round}", finishedRound.ToString()).Replace("{crashed}", crashedText)
                .Replace("{left}", alive.Count.ToString()).Replace("{next}", round.ToString())
                .Replace("{secs}", Config.RoundSeconds.ToString()));
        }
    }

    private void Win(CrashPlayer p)
    {
        winner = p;
        phase = Phase.Done;
        var lead = pendingCrashText.Count > 0 ? string.Join(" ", pendingCrashText) + " " : string.Empty;
        pendingCrashText.Clear();
        Say(lead + Config.WinnerTemplate.Replace("{name}", p.Name).Replace("{roll}", p.LastRoll.ToString()).Replace("{prize}", PrizeOrDefault));
        Config.History.Insert(0, new CrashRecord
        {
            When = DateTime.Now, Prize = PrizeOrDefault, Players = startingPlayers, Rounds = round,
            Winner = string.IsNullOrEmpty(p.World) ? p.Name : $"{p.Name} ({p.World})",
        });
        if (Config.History.Count > 200) Config.History.RemoveRange(200, Config.History.Count - 200);
        plugin.Save();
    }

    private void Crash(CrashPlayer p, string how)
    {
        alive.Remove(p.Key);
        p.CrashedRound = round; p.How = how;
        crashed.Add(p);
    }

    private void Revive(CrashPlayer p)
    {
        crashed.Remove(p);
        alive[p.Key] = p;
        if (alive.Count > 2) finalRound = false;
    }

    private string CrashText(CrashPlayer p) => Config.CrashedText.Replace("{name}", p.Name).Replace("{roll}", p.LastRoll.ToString());

    private static string JoinNames(List<string> n) => n.Count switch
    {
        0 => string.Empty,
        1 => n[0],
        2 => $"{n[0]} and {n[1]}",
        _ => string.Join(", ", n.Take(n.Count - 1)) + ", and " + n[^1],
    };

    private void TellWinner()
    {
        if (winner == null) return;
        var target = string.IsNullOrEmpty(winner.World) ? winner.Name : $"{winner.Name}@{winner.World}";
        var msg = $"Congrats, you won {PrizeOrDefault} in System Crash! Come find me to claim it.";
        lastSent = Chat.Clean(msg);
        Chat.Send($"/tell {target} {msg}");
    }

    private void Say(string text)
    {
        lastSent = Chat.Clean(text);
        plugin.Announce(text);
    }

    // ======================= UI =======================
    public void Draw()
    {
        if (ImGui.BeginTabBar("##crashtabs"))
        {
            if (ImGui.BeginTabItem("Host")) { DrawHost(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("History")) { DrawHistory(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Settings")) { DrawSettings(); ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }
    }

    private void DrawHost()
    {
        if (phase == Phase.Idle || phase == Phase.Done)
        {
            if (phase == Phase.Done && winner != null)
            {
                ImGui.TextColored(Green, $"Winner: {winner.Name} - {PrizeOrDefault}");
                if (ImGui.Button("Send them a /tell")) TellWinner();
                ImGui.Separator();
            }
            ImGui.TextWrapped("Everyone who /randoms in round 1 is in. Each round the lowest roll crashes out and can't roll again. When two are left, the highest roll wins.");
            var p = Config.Prize;
            ImGui.SetNextItemWidth(260);
            if (ImGui.InputText("Prize", ref p, 120)) { Config.Prize = p; plugin.Save(); }
            ImGui.SetNextItemWidth(100);
            var s = Config.RoundSeconds;
            if (ImGui.InputInt("Seconds per round", ref s, 5)) { Config.RoundSeconds = Math.Max(10, s); plugin.Save(); }
            var auto = Config.AutoAdvance;
            if (ImGui.Checkbox("Announce eliminations and start the next round automatically", ref auto)) { Config.AutoAdvance = auto; plugin.Save(); }
            ImGui.Spacing();
            if (ImGui.Button("Start System Crash + shout")) Start();
            DrawLastSent();
            return;
        }

        // ---- running ----
        ImGui.TextColored(Pink, $"Prize: {PrizeOrDefault}");
        var label = tie switch
        {
            Tie.Lowest => "TIE-BREAK (lowest crashes)",
            Tie.Final => "FINAL TIE-BREAK",
            _ => finalRound ? "FINAL ROUND - highest wins" : $"Round {round}" + (round == 1 ? " - sign-up round" : string.Empty),
        };
        ImGui.Text(label);
        if (phase == Phase.Countdown) ImGui.TextColored(Gold, $"Counting down... {countdown.Showing}");
        else if (phase == Phase.Rolling) ImGui.TextColored(Green, $"Taking rolls - {SecondsLeft}s left - {rolls.Count} rolled");
        else ImGui.TextColored(Grey, $"Rolls closed - {rolls.Count} rolled");

        if (phase == Phase.Rolling && ImGui.Button("End round now")) Resolve();
        if (phase == Phase.Closed && ImGui.Button("Announce results + next round")) Resolve();
        ImGui.SameLine();
        if (ImGui.Button("Stop game") && ImGui.GetIO().KeyCtrl) { countdown.Cancel(); phase = Phase.Idle; }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Hold Ctrl and click (no shout)");
        if (ignoredCrashed > 0 || ignoredOther > 0)
            ImGui.TextColored(Grey, $"Ignored rolls: {ignoredCrashed} from crashed players, {ignoredOther} other (not playing / custom range).");

        // who's in this period
        var inPlay = round == 1 && tie == Tie.None
            ? entrants.Values.ToList()
            : alive.Values.Where(p => tie == Tie.None || tieSet.Contains(p.Key)).ToList();

        ImGui.Separator();
        ImGui.Text(round == 1 && tie == Tie.None ? $"Signed up: {inPlay.Count}" : $"Still online: {alive.Count}");
        if (ImGui.BeginTable("##alive", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY, new Vector2(0, 200)))
        {
            ImGui.TableSetupColumn("Player", ImGuiTableColumnFlags.WidthStretch, 0);
            ImGui.TableSetupColumn("Roll", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("##kick", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableHeadersRow();
            var min = rolls.Count > 0 ? rolls.Values.Min() : -1;
            CrashPlayer? kick = null;
            foreach (var p in inPlay.OrderBy(p => rolls.TryGetValue(p.Key, out var v) ? v : int.MaxValue))
            {
                ImGui.PushID(p.Key);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                var has = rolls.TryGetValue(p.Key, out var val);
                if (has && val == min && !finalRound && tie != Tie.Final) ImGui.TextColored(Red, p.Name); else ImGui.Text(p.Name);
                if (!string.IsNullOrEmpty(p.World)) { ImGui.SameLine(); ImGui.TextColored(Grey, p.World); }
                ImGui.TableNextColumn(); if (has) ImGui.Text(val.ToString()); else ImGui.TextColored(Grey, "-");
                ImGui.TableNextColumn();
                if (!(round == 1 && tie == Tie.None) && ImGui.SmallButton("Crash")) kick = p;
                ImGui.PopID();
            }
            ImGui.EndTable();
            if (kick != null) Crash(kick, "removed by host");
        }

        if (crashed.Count > 0 && ImGui.CollapsingHeader($"Crashed ({crashed.Count})"))
        {
            CrashPlayer? revive = null;
            foreach (var c in crashed)
            {
                ImGui.PushID("c" + c.Key);
                ImGui.TextColored(Grey, $"R{c.CrashedRound}  {c.Name} - {c.How}");
                ImGui.SameLine();
                if (ImGui.SmallButton("Revive")) revive = c;
                ImGui.PopID();
            }
            if (revive != null) Revive(revive);
        }
        DrawLastSent();
    }

    private void DrawLastSent()
    {
        if (string.IsNullOrEmpty(lastSent)) return;
        ImGui.Spacing();
        ImGui.TextColored(Grey, "Last message sent: " + lastSent);
    }

    private void DrawHistory()
    {
        if (Config.History.Count == 0) { ImGui.TextColored(Grey, "No games yet."); return; }
        if (ImGui.BeginTable("##chist", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY, new Vector2(0, 320)))
        {
            ImGui.TableSetupColumn("When", ImGuiTableColumnFlags.WidthFixed, 110);
            ImGui.TableSetupColumn("Prize", ImGuiTableColumnFlags.WidthStretch, 0);
            ImGui.TableSetupColumn("Winner", ImGuiTableColumnFlags.WidthFixed, 150);
            ImGui.TableSetupColumn("Players", ImGuiTableColumnFlags.WidthFixed, 55);
            ImGui.TableSetupColumn("Rounds", ImGuiTableColumnFlags.WidthFixed, 55);
            ImGui.TableHeadersRow();
            foreach (var h in Config.History)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.Text(h.When.ToString("MM/dd HH:mm"));
                ImGui.TableNextColumn(); ImGui.TextWrapped(h.Prize);
                ImGui.TableNextColumn(); ImGui.Text(h.Winner);
                ImGui.TableNextColumn(); ImGui.Text(h.Players.ToString());
                ImGui.TableNextColumn(); ImGui.Text(h.Rounds.ToString());
            }
            ImGui.EndTable();
        }
        if (ImGui.Button("Clear history") && ImGui.GetIO().KeyCtrl) { Config.History.Clear(); plugin.Save(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Hold Ctrl and click");
    }

    private void DrawSettings()
    {
        var changed = false;
        var t = Config.TieBreakSeconds;
        ImGui.SetNextItemWidth(100);
        if (ImGui.InputInt("Seconds for a tie-break roll", ref t, 5)) { Config.TieBreakSeconds = Math.Max(10, t); changed = true; }
        var nr = Config.NoRollIsEliminated;
        if (ImGui.Checkbox("Players who don't roll in a round crash out", ref nr)) { Config.NoRollIsEliminated = nr; changed = true; }
        var std = Config.OnlyStandardRolls;
        if (ImGui.Checkbox("Only count normal /random rolls (ignore /random 10 etc.)", ref std)) { Config.OnlyStandardRolls = std; changed = true; }

        ImGui.Separator();
        ImGui.Text("Messages");
        ImGui.TextColored(Grey, "Placeholders: {prize} {secs} {round} {next} {left} {crashed} {name} {roll} {names} {a} {b}");
        changed |= Field("Start", () => Config.StartTemplate, v => Config.StartTemplate = v);
        changed |= Field("Round results", () => Config.RoundTemplate, v => Config.RoundTemplate = v);
        changed |= Field("Final round", () => Config.FinalTemplate, v => Config.FinalTemplate = v);
        changed |= Field("Crashed", () => Config.CrashedText, v => Config.CrashedText = v);
        changed |= Field("No roll", () => Config.NoRollText, v => Config.NoRollText = v);
        changed |= Field("Tie", () => Config.TieTemplate, v => Config.TieTemplate = v);
        changed |= Field("Winner", () => Config.WinnerTemplate, v => Config.WinnerTemplate = v);
        changed |= Field("Too few players", () => Config.TooFewTemplate, v => Config.TooFewTemplate = v);
        if (changed) plugin.Save();
    }

    private static bool Field(string label, Func<string> get, Action<string> set)
    {
        var v = get();
        ImGui.SetNextItemWidth(-120);
        if (!ImGui.InputText(label, ref v, 400)) return false;
        set(v); return true;
    }
}
