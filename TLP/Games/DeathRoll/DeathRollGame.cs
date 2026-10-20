using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;

namespace TLP.Games.DeathRoll;

public class DeathPlayer
{
    public string Name = string.Empty;
    public string World = string.Empty;
    public bool IsHouse;
    public string Display => string.IsNullOrEmpty(World) ? Name : $"{Name} ({World})";

    public bool Matches(RollInfo r)
    {
        if (IsHouse) return r.IsSelf;
        if (!r.Name.Equals(Name, StringComparison.OrdinalIgnoreCase)) return false;
        return string.IsNullOrEmpty(World) || string.IsNullOrEmpty(r.World) || r.World.Equals(World, StringComparison.OrdinalIgnoreCase);
    }
}

public class DeathRollEntry
{
    public string Name = string.Empty;
    public int Max;
    public int Value;
    public DateTime Time;
    public string Note = string.Empty;   // empty = counted
    public bool Counted;
    public int PlayerIndex = -1;
}

public class TMatch
{
    public DeathPlayer A = null!;
    public DeathPlayer B = null!;
    public int WinnerSide = -1;   // 0 = A, 1 = B
    public bool Forfeit;
    public DeathPlayer? Winner => WinnerSide == 0 ? A : WinnerSide == 1 ? B : null;
    public DeathPlayer? Loser => WinnerSide == 0 ? B : WinnerSide == 1 ? A : null;
}

public class TRound
{
    public int Number;
    public List<TMatch> Matches = new();
    public DeathPlayer? Bye;
    public bool Done => Matches.All(m => m.WinnerSide >= 0);
}

/// <summary>
/// Two players take turns: each /randoms the number the other just rolled. Whoever rolls a 1 loses.
/// Rolls of the wrong range or out of turn are shown but not counted (the host can count one anyway).
/// The host can play as the house.
/// </summary>
public class DeathRollGame : IGame
{
    private readonly Plugin plugin;
    private DeathRollConfig Config => plugin.Config.DeathRoll;
    public string Name => "Death Roll";

    private enum Phase { Setup, Playing, Over }
    private Phase phase = Phase.Setup;

    private readonly DeathPlayer?[] players = new DeathPlayer?[2];
    private int armedSlot = -1;              // waiting for the next /random to fill this slot
    private readonly string[] typedName = { string.Empty, string.Empty };

    private int turn = -1;                    // -1 = whoever rolls first
    private int currentMax;
    private int wager;
    private int start;
    private readonly List<DeathRollEntry> feed = new();
    private int winnerIdx = -1, loserIdx = -1;
    private string lastSent = string.Empty;
    private readonly Random rng = new();

    // tournament
    private readonly List<DeathPlayer> tEntrants = new();
    private readonly List<TRound> tRounds = new();
    private readonly HashSet<string> hadBye = new(StringComparer.OrdinalIgnoreCase);
    private bool tActive;
    private bool tSignup;
    private TMatch? tCurrent;
    private DeathPlayer? champion;
    private string tTyped = string.Empty;

    private static readonly Vector4 Green = new(0.45f, 1f, 0.55f, 1f);
    private static readonly Vector4 Gold = new(1f, 0.82f, 0.3f, 1f);
    private static readonly Vector4 Grey = new(0.65f, 0.65f, 0.7f, 1f);
    private static readonly Vector4 Pink = new(1f, 0.4f, 0.8f, 1f);
    private static readonly Vector4 Red = new(1f, 0.4f, 0.4f, 1f);

    public DeathRollGame(Plugin plugin) => this.plugin = plugin;
    public void Dispose() { }
    public void Update() { }

    // ======================= chat =======================
    public void OnChatMessage(XivChatType type, SeString sender, SeString message)
    {
        if (phase == Phase.Over) return;
        if (!Rolls.TryParse(type, sender, message, true, out var r)) return;

        if (!tActive && tSignup && phase != Phase.Playing)
        {
            AddEntrant(new DeathPlayer { Name = r.Name, World = r.World, IsHouse = r.IsSelf });
            return;
        }

        if (phase != Phase.Playing)
        {
            if (armedSlot < 0) return;
            var other = players[1 - armedSlot];
            if (other != null && other.Matches(r)) return;   // same person can't fill both seats
            players[armedSlot] = new DeathPlayer { Name = r.Name, World = r.World, IsHouse = r.IsSelf };
            armedSlot = -1;
            return;
        }

        // Playing
        var idx = players[0]!.Matches(r) ? 0 : players[1]!.Matches(r) ? 1 : -1;
        if (idx < 0) return;   // not in this match

        var entry = new DeathRollEntry { Name = players[idx]!.Name, Max = r.Max, Value = r.Value, Time = DateTime.Now, PlayerIndex = idx };
        feed.Add(entry);

        if (turn >= 0 && idx != turn)
        {
            entry.Note = "not their turn";
            return;
        }
        if (r.Max != currentMax)
        {
            entry.Note = $"wrong range (needed {currentMax})";
            if (Config.RemindWrongRange)
                Say(Fill(Config.WrongRangeTemplate, name: players[idx]!.Name, max: currentMax));
            return;
        }
        Accept(entry);
    }

    private void Accept(DeathRollEntry e)
    {
        e.Counted = true;
        e.Note = string.Empty;
        var idx = e.PlayerIndex;
        currentMax = e.Value;

        if (e.Value <= 1)
        {
            loserIdx = idx;
            winnerIdx = 1 - idx;
            phase = Phase.Over;
            var w = players[winnerIdx]!; var l = players[loserIdx]!;
            if (tCurrent != null) RecordTournamentResult(winnerIdx, false);
            else Say(Fill(Config.LoserTemplate, winner: w.Name, loser: l.Name));
            Config.History.Insert(0, new DeathRecord
            {
                When = DateTime.Now, A = players[0]!.Name, B = players[1]!.Name, Winner = w.Name, Loser = l.Name,
                Wager = wager, Start = start, Rolls = CountedRolls,
                HouseInvolved = w.IsHouse || l.IsHouse, HouseWon = w.IsHouse,
            });
            if (Config.History.Count > 200) Config.History.RemoveRange(200, Config.History.Count - 200);
            plugin.Save();
            return;
        }

        turn = 1 - idx;
        if (Config.AnnounceEachRoll)
            Say(Fill(Config.RollTemplate, name: players[idx]!.Name, roll: e.Value, next: players[turn]!.Name));
    }

    private int CountedRolls => feed.Count(f => f.Counted);

    // ======================= flow =======================
    private void StartMatch(int firstTurn, string? announce = null)
    {
        if (players[0] == null || players[1] == null) return;
        start = Math.Clamp(Config.StartNumber, 2, 999999);
        wager = tCurrent != null ? 0 : Math.Max(0, Config.Wager);
        currentMax = start;
        turn = firstTurn;
        feed.Clear();
        winnerIdx = loserIdx = -1;
        phase = Phase.Playing;
        if (announce != null) { Say(announce); return; }
        var tpl = turn < 0 ? Config.StartAnyTemplate : Config.StartTemplate;
        Say(Fill(tpl, next: turn >= 0 ? players[turn]!.Name : string.Empty));
    }

    private void UndoLast()
    {
        var last = feed.LastOrDefault(f => f.Counted);
        if (last == null) return;
        feed.Remove(last);
        var prev = feed.LastOrDefault(f => f.Counted);
        currentMax = prev?.Value ?? start;
        turn = last.PlayerIndex;   // it's that player's turn again
        if (phase == Phase.Over)
        {
            phase = Phase.Playing; winnerIdx = loserIdx = -1;
            if (Config.History.Count > 0) Config.History.RemoveAt(0);
            if (tCurrent != null) { tCurrent.WinnerSide = -1; tCurrent.Forfeit = false; champion = null; }
            plugin.Save();
        }
    }

    private void ShoutTurn()
    {
        if (turn < 0) { Say($"Death roll: whoever goes first, /random {currentMax}"); return; }
        Say($"{players[turn]!.Name}, you're up: /random {currentMax}");
    }

    // ======================= tournament =======================
    private void AddEntrant(DeathPlayer p)
    {
        if (string.IsNullOrWhiteSpace(p.Name)) return;
        if (tEntrants.Any(e => e.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase))) return;
        tEntrants.Add(p);
    }

    private string T(string t, int round = 0, int match = 0, string bye = "", string pairs = "")
        => t.Replace("{round}", round.ToString()).Replace("{match}", match.ToString())
            .Replace("{prize}", Config.TournamentPrize).Replace("{count}", tEntrants.Count.ToString())
            .Replace("{bye}", bye).Replace("{pairs}", pairs);

    private void StartTournament()
    {
        if (tEntrants.Count < 2) return;
        tRounds.Clear(); hadBye.Clear(); champion = null; tCurrent = null;
        tActive = true; tSignup = false;
        phase = Phase.Setup;
        Say(T(Config.TStartTemplate));
        BuildRound(tEntrants.ToList());
    }

    /// <summary>Shuffles the survivors into pairs. With an odd count one random player (preferring someone
    /// who hasn't had a bye yet) skips straight to the next round.</summary>
    private void BuildRound(List<DeathPlayer> survivors)
    {
        var pool = survivors.OrderBy(_ => rng.Next()).ToList();
        var round = new TRound { Number = tRounds.Count + 1 };
        if (pool.Count % 2 == 1)
        {
            var candidates = pool.Where(p => !hadBye.Contains(p.Name)).ToList();
            if (candidates.Count == 0) candidates = pool;
            var bye = candidates[rng.Next(candidates.Count)];
            pool.Remove(bye);
            round.Bye = bye;
            hadBye.Add(bye.Name);
        }
        for (var i = 0; i + 1 < pool.Count; i += 2)
            round.Matches.Add(new TMatch { A = pool[i], B = pool[i + 1] });
        tRounds.Add(round);
        AnnounceRound(round);
    }

    private void AnnounceRound(TRound round)
    {
        // Split the bracket over several messages so none gets cut off.
        var pairs = round.Matches.Select(m => $"{m.A.Name} vs {m.B.Name}").ToList();
        var lines = new List<string>();
        var cur = string.Empty;
        foreach (var p in pairs)
        {
            var next = cur.Length == 0 ? p : cur + " | " + p;
            if (System.Text.Encoding.UTF8.GetByteCount(T(Config.TRoundTemplate, round.Number, pairs: next)) > 420 && cur.Length > 0)
            { lines.Add(cur); cur = p; }
            else cur = next;
        }
        if (cur.Length > 0) lines.Add(cur);
        foreach (var l in lines) Say(T(Config.TRoundTemplate, round.Number, pairs: l));
        if (round.Bye != null) Say(T(Config.TByeTemplate, round.Number, bye: round.Bye.Name));
    }

    private TRound? CurrentRound => tRounds.LastOrDefault();
    private TMatch? NextMatch => CurrentRound?.Matches.FirstOrDefault(m => m.WinnerSide < 0);

    private List<DeathPlayer> Survivors(TRound r)
    {
        var list = r.Matches.Select(m => m.Winner!).ToList();
        if (r.Bye != null) list.Add(r.Bye);
        return list;
    }

    /// <summary>Plays the next match, building the next round first if this one is finished.</summary>
    private void PlayNextMatch()
    {
        if (!tActive || champion != null || CurrentRound == null) return;
        if (CurrentRound.Done) BuildRound(Survivors(CurrentRound));
        var m = NextMatch;
        if (m == null) return;
        tCurrent = m;
        players[0] = m.A; players[1] = m.B;
        start = Math.Clamp(Config.StartNumber, 2, 999999);
        var first = Config.RandomFirstInTournament ? rng.Next(2) : 0;
        var idx = CurrentRound!.Matches.IndexOf(m) + 1;
        StartMatch(first, T(Fill(Config.TMatchTemplate, next: players[first]!.Name), CurrentRound.Number, idx));
    }

    private void RecordTournamentResult(int winnerSide, bool forfeit)
    {
        if (tCurrent == null || CurrentRound == null) return;
        tCurrent.WinnerSide = winnerSide;
        tCurrent.Forfeit = forfeit;
        var w = tCurrent.Winner!; var l = tCurrent.Loser!;
        Say(forfeit ? $"{l.Name} forfeits -- {w.Name} advances!" : T(Fill(Config.TResultTemplate, winner: w.Name, loser: l.Name), CurrentRound.Number));

        if (CurrentRound.Done)
        {
            var left = Survivors(CurrentRound);
            if (left.Count == 1)
            {
                champion = left[0];
                Say(T(Fill(Config.TChampionTemplate, winner: champion.Name), CurrentRound.Number));
            }
        }
    }

    private void Forfeit(int winnerSide)
    {
        if (tCurrent == null) return;
        winnerIdx = winnerSide; loserIdx = 1 - winnerSide;
        phase = Phase.Over;
        RecordTournamentResult(winnerSide, true);
    }

    private void EndTournament()
    {
        tActive = false; tCurrent = null; champion = null;
        tRounds.Clear(); hadBye.Clear();
        phase = Phase.Setup;
        players[0] = players[1] = null;
    }

    private string Fill(string t, string name = "", int roll = 0, string next = "", int max = 0, string winner = "", string loser = "")
        => t.Replace("{a}", players[0]?.Name ?? "?").Replace("{b}", players[1]?.Name ?? "?")
            .Replace("{wager}", wager.ToString("N0")).Replace("{pot}", ((long)wager * 2).ToString("N0"))
            .Replace("{start}", start.ToString()).Replace("{name}", name).Replace("{roll}", roll.ToString())
            .Replace("{next}", next).Replace("{max}", max.ToString())
            .Replace("{winner}", winner).Replace("{loser}", loser).Replace("{rolls}", CountedRolls.ToString());

    private void Say(string text)
    {
        lastSent = Chat.Clean(text);
        plugin.Announce(text);
    }

    // ======================= UI =======================
    public void Draw()
    {
        if (ImGui.BeginTabBar("##drtabs"))
        {
            if (ImGui.BeginTabItem("1 vs 1")) { DrawHost(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Tournament")) { DrawTournament(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("History")) { DrawHistory(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Settings")) { DrawSettings(); ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }
    }

    private void DrawHost()
    {
        if (tActive) { ImGui.TextColored(Gold, "A tournament is running -- use the Tournament tab."); return; }
        if (phase == Phase.Setup) DrawSetup();
        else DrawMatch();
        if (!string.IsNullOrEmpty(lastSent)) { ImGui.Spacing(); ImGui.TextColored(Grey, "Last message sent: " + lastSent); }
    }

    private void DrawSetup()
    {
        ImGui.TextWrapped("Two players take turns. Each one /randoms the number the other just rolled. Whoever rolls a 1 loses.");
        ImGui.Spacing();
        for (var i = 0; i < 2; i++)
        {
            ImGui.PushID("slot" + i);
            ImGui.TextColored(Pink, i == 0 ? "Player 1:" : "Player 2:");
            ImGui.SameLine();
            if (players[i] != null)
            {
                ImGui.TextColored(Gold, players[i]!.Display + (players[i]!.IsHouse ? "  [house - you]" : string.Empty));
                ImGui.SameLine();
                if (ImGui.SmallButton("Clear")) players[i] = null;
            }
            else if (armedSlot == i)
            {
                ImGui.TextColored(Green, "Waiting for the next /random...");
                ImGui.SameLine();
                if (ImGui.SmallButton("Cancel")) armedSlot = -1;
            }
            else
            {
                if (ImGui.SmallButton("Me (house)")) SetHouse(i);
                ImGui.SameLine();
                if (ImGui.SmallButton("My target")) SetFromTarget(i);
                ImGui.SameLine();
                if (ImGui.SmallButton("Next roller")) armedSlot = i;
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("The next person to /random takes this seat.");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(150);
                ImGui.InputText("##typed", ref typedName[i], 40);
                ImGui.SameLine();
                if (ImGui.SmallButton("Set") && typedName[i].Trim().Length > 0)
                    players[i] = new DeathPlayer { Name = typedName[i].Trim() };
            }
            ImGui.PopID();
        }

        ImGui.Spacing();
        var w = Config.Wager;
        ImGui.SetNextItemWidth(160);
        if (ImGui.InputInt("Wager (gil each)", ref w, 50_000)) { Config.Wager = Math.Clamp(w, 0, 999_999_999); plugin.Save(); }
        ImGui.SameLine();
        ImGui.TextColored(Grey, $"Pot: {((long)Config.Wager * 2):N0}");
        var s = Config.StartNumber;
        ImGui.SetNextItemWidth(160);
        if (ImGui.InputInt("Start at", ref s, 100)) { Config.StartNumber = Math.Clamp(s, 2, 999999); plugin.Save(); }

        ImGui.Spacing();
        var ready = players[0] != null && players[1] != null;
        ImGui.BeginDisabled(!ready);
        if (ImGui.Button("Start - Player 1 goes first")) StartMatch(0);
        ImGui.SameLine();
        if (ImGui.Button("Start - Player 2 goes first")) StartMatch(1);
        ImGui.SameLine();
        if (ImGui.Button("Start - whoever rolls first")) StartMatch(-1);
        ImGui.EndDisabled();
        if (!ready) ImGui.TextColored(Grey, "Fill both seats to start.");
    }

    private void SetHouse(int i)
    {
        var me = Plugin.ObjectTable.LocalPlayer;
        if (me == null) return;
        if (players[1 - i]?.IsHouse == true) return;
        var world = string.Empty;
        try { world = me.HomeWorld.Value.Name.ExtractText(); } catch { }
        players[i] = new DeathPlayer { Name = me.Name.TextValue, World = world, IsHouse = true };
    }

    private void SetFromTarget(int i)
    {
        if (Plugin.TargetManager.Target is not IPlayerCharacter pc) return;
        var world = string.Empty;
        try { world = pc.HomeWorld.Value.Name.ExtractText(); } catch { }
        var me = Plugin.ObjectTable.LocalPlayer;
        var isMe = me != null && me.Name.TextValue == pc.Name.TextValue;
        players[i] = new DeathPlayer { Name = pc.Name.TextValue, World = world, IsHouse = isMe };
    }

    private void DrawMatch()
    {
        ImGui.TextColored(Pink, $"{players[0]!.Display}  vs  {players[1]!.Display}");
        ImGui.TextColored(Grey, $"Wager {wager:N0} each - pot {(long)wager * 2:N0} - started at {start}");

        if (phase == Phase.Playing)
        {
            var who = turn < 0 ? "Whoever goes first" : players[turn]!.Name;
            ImGui.TextColored(Gold, $"{who}'s turn:  /random {currentMax}");
            if (ImGui.Button("Re-shout whose turn")) ShoutTurn();
            ImGui.SameLine();
            ImGui.BeginDisabled(CountedRolls == 0);
            if (ImGui.Button("Undo last roll")) UndoLast();
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (tCurrent == null)
            {
                if (ImGui.Button("Cancel match") && ImGui.GetIO().KeyCtrl) phase = Phase.Setup;
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Hold Ctrl and click");
            }
            else
            {
                if (ImGui.Button($"{players[0]!.Name} wins (forfeit)")) Forfeit(0);
                ImGui.SameLine();
                if (ImGui.Button($"{players[1]!.Name} wins (forfeit)")) Forfeit(1);
            }
        }
        else
        {
            ImGui.TextColored(Red, $"{players[loserIdx]!.Name} rolled the 1!");
            ImGui.TextColored(Green, $"{players[winnerIdx]!.Name} wins {(long)wager * 2:N0} gil");
            if (tCurrent == null)
            {
                if (ImGui.Button("Rematch")) StartMatch(loserIdx);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Same players and wager; the loser goes first.");
                ImGui.SameLine();
                if (ImGui.Button("New match")) phase = Phase.Setup;
                ImGui.SameLine();
            }
            if (!(tCurrent?.Forfeit ?? false) && ImGui.Button("Undo the losing roll")) UndoLast();
            if (tCurrent == null && !players[winnerIdx]!.IsHouse)
            {
                ImGui.SameLine();
                if (ImGui.Button("/tell winner"))
                {
                    var p = players[winnerIdx]!;
                    var target = string.IsNullOrEmpty(p.World) ? p.Name : $"{p.Name}@{p.World}";
                    plugin.SendLine($"/tell {target} You won the death roll! Come see me for your {(long)wager * 2:N0} gil.");
                }
            }
        }

        ImGui.Separator();
        if (ImGui.BeginTable("##drfeed", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY, new Vector2(0, Math.Max(120f, ImGui.GetContentRegionAvail().Y - 30f))))
        {
            ImGui.TableSetupColumn("Player", ImGuiTableColumnFlags.WidthStretch, 0);
            ImGui.TableSetupColumn("Range", ImGuiTableColumnFlags.WidthFixed, 70);
            ImGui.TableSetupColumn("Roll", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("##st", ImGuiTableColumnFlags.WidthFixed, 230);
            ImGui.TableHeadersRow();
            DeathRollEntry? countAnyway = null;
            for (var i = feed.Count - 1; i >= 0; i--)
            {
                var e = feed[i];
                ImGui.PushID("e" + i);
                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.Text(e.Name);
                ImGui.TableNextColumn(); ImGui.TextColored(Grey, $"1-{e.Max}");
                ImGui.TableNextColumn();
                if (e.Counted) ImGui.TextColored(e.Value <= 1 ? Red : Green, e.Value.ToString()); else ImGui.TextColored(Grey, e.Value.ToString());
                ImGui.TableNextColumn();
                if (e.Counted) ImGui.TextColored(Green, "counted");
                else
                {
                    ImGui.TextColored(Red, e.Note);
                    if (phase == Phase.Playing && i == feed.Count - 1) { ImGui.SameLine(); if (ImGui.SmallButton("Count it")) countAnyway = e; }
                }
                ImGui.PopID();
            }
            ImGui.EndTable();
            if (countAnyway != null) Accept(countAnyway);
        }
    }

    private void DrawTournament()
    {
        if (!tActive)
        {
            if (phase == Phase.Playing) { ImGui.TextColored(Gold, "Finish the 1 vs 1 match first."); return; }
            ImGui.TextWrapped("Add everyone who's playing. TLP shuffles them into pairs; only winners advance. With an odd number, one random player gets a bye each round.");
            ImGui.Spacing();
            ImGui.SetNextItemWidth(200);
            var enter = ImGui.InputText("##tname", ref tTyped, 40, ImGuiInputTextFlags.EnterReturnsTrue);
            ImGui.SameLine();
            if ((ImGui.Button("Add name") || enter) && tTyped.Trim().Length > 0) { AddEntrant(new DeathPlayer { Name = tTyped.Trim() }); tTyped = string.Empty; }
            ImGui.SameLine();
            if (ImGui.Button("Add my target") && Plugin.TargetManager.Target is IPlayerCharacter pc)
            {
                var world = string.Empty;
                try { world = pc.HomeWorld.Value.Name.ExtractText(); } catch { }
                var me = Plugin.ObjectTable.LocalPlayer;
                AddEntrant(new DeathPlayer { Name = pc.Name.TextValue, World = world, IsHouse = me != null && me.Name.TextValue == pc.Name.TextValue });
            }
            ImGui.SameLine();
            if (ImGui.Button("Add me"))
            {
                var me = Plugin.ObjectTable.LocalPlayer;
                if (me != null) AddEntrant(new DeathPlayer { Name = me.Name.TextValue, IsHouse = true });
            }
            ImGui.Checkbox("Sign-ups open: anyone who types /random is added", ref tSignup);

            ImGui.TextColored(Pink, $"Players ({tEntrants.Count})");
            DeathPlayer? remove = null;
            foreach (var e in tEntrants)
            {
                ImGui.PushID("te" + e.Name);
                if (ImGui.SmallButton("x")) remove = e;
                ImGui.SameLine(); ImGui.Text(e.Display + (e.IsHouse ? "  [you]" : string.Empty));
                ImGui.PopID();
            }
            if (remove != null) tEntrants.Remove(remove);
            if (tEntrants.Count > 0 && ImGui.SmallButton("Clear list") && ImGui.GetIO().KeyCtrl) tEntrants.Clear();

            ImGui.Spacing();
            var prize = Config.TournamentPrize;
            ImGui.SetNextItemWidth(220);
            if (ImGui.InputText("Champion's prize", ref prize, 80)) { Config.TournamentPrize = prize; plugin.Save(); }
            var s = Config.StartNumber;
            ImGui.SetNextItemWidth(160);
            if (ImGui.InputInt("Start at", ref s, 100)) { Config.StartNumber = Math.Clamp(s, 2, 999999); plugin.Save(); }
            var rf = Config.RandomFirstInTournament;
            if (ImGui.Checkbox("Randomly pick who rolls first in each match", ref rf)) { Config.RandomFirstInTournament = rf; plugin.Save(); }

            ImGui.BeginDisabled(tEntrants.Count < 2);
            if (ImGui.Button("Start tournament + shout bracket")) StartTournament();
            ImGui.EndDisabled();
            return;
        }

        // ---- running ----
        if (champion != null)
        {
            ImGui.TextColored(Green, $"CHAMPION: {champion.Name}  -  {Config.TournamentPrize}");
            if (ImGui.Button("Shout champion again")) Say(T(Fill(Config.TChampionTemplate, winner: champion.Name), CurrentRound?.Number ?? 0));
            ImGui.SameLine();
            if (!champion.IsHouse && ImGui.Button("/tell champion"))
            {
                var target = string.IsNullOrEmpty(champion.World) ? champion.Name : $"{champion.Name}@{champion.World}";
                plugin.SendLine($"/tell {target} You won the death roll tournament! Come see me for your prize: {Config.TournamentPrize}");
            }
            ImGui.SameLine();
            if (ImGui.Button("Undo final roll") && phase == Phase.Over) UndoLast();
            ImGui.SameLine();
            if (ImGui.Button("Finish tournament")) EndTournament();
        }
        else if (phase == Phase.Playing)
        {
            DrawMatch();
        }
        else
        {
            var r = CurrentRound!;
            var m = NextMatch;
            if (phase == Phase.Over && tCurrent != null) DrawLastResult();
            if (m != null)
            {
                if (ImGui.Button($"Start next match: {m.A.Name} vs {m.B.Name}")) PlayNextMatch();
            }
            else if (r.Done)
            {
                if (ImGui.Button($"Start round {r.Number + 1} (shouts the new bracket)")) PlayNextMatch();
            }
            ImGui.SameLine();
            if (ImGui.Button("Re-shout bracket")) AnnounceRound(r);
            ImGui.SameLine();
            if (ImGui.Button("Cancel tournament") && ImGui.GetIO().KeyCtrl) EndTournament();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Hold Ctrl and click");
        }

        ImGui.Separator();
        DrawBracket();
    }

    private void DrawLastResult()
    {
        if (tCurrent?.Winner == null) return;
        ImGui.TextColored(Green, $"{tCurrent.Winner.Name} beat {tCurrent.Loser!.Name}{(tCurrent.Forfeit ? " (forfeit)" : string.Empty)}");
        if (!tCurrent.Forfeit) { ImGui.SameLine(); if (ImGui.SmallButton("Undo the losing roll")) UndoLast(); }
    }

    private void DrawBracket()
    {
        ImGui.BeginChild("##bracket", new Vector2(0, 0), false);
        for (var i = tRounds.Count - 1; i >= 0; i--)
        {
            var r = tRounds[i];
            ImGui.TextColored(Pink, $"Round {r.Number}" + (r.Done ? "  (done)" : string.Empty));
            for (var j = 0; j < r.Matches.Count; j++)
            {
                var m = r.Matches[j];
                var playing = ReferenceEquals(m, tCurrent) && phase == Phase.Playing;
                ImGui.Text($"  {j + 1}.");
                ImGui.SameLine();
                Side(m.A, m.WinnerSide, 0);
                ImGui.SameLine(); ImGui.TextColored(Grey, "vs"); ImGui.SameLine();
                Side(m.B, m.WinnerSide, 1);
                if (playing) { ImGui.SameLine(); ImGui.TextColored(Gold, "<- playing now"); }
                if (m.Forfeit) { ImGui.SameLine(); ImGui.TextColored(Grey, "(forfeit)"); }
            }
            if (r.Bye != null) ImGui.TextColored(Gold, $"  Bye: {r.Bye.Name}");
            ImGui.Spacing();
        }
        ImGui.EndChild();
    }

    private void Side(DeathPlayer p, int winnerSide, int side)
    {
        if (winnerSide < 0) ImGui.Text(p.Name);
        else if (winnerSide == side) ImGui.TextColored(Green, p.Name);
        else ImGui.TextColored(Red, p.Name + " (out)");
    }

    private void DrawHistory()
    {
        var h = Config.History;
        var house = h.Where(x => x.HouseInvolved).ToList();
        if (house.Count > 0)
        {
            var won = house.Count(x => x.HouseWon);
            long net = house.Sum(x => x.HouseWon ? x.Wager : -x.Wager);
            ImGui.TextColored(Gold, $"House: {won} won / {house.Count - won} lost - net {(net >= 0 ? "+" : "")}{net:N0} gil");
        }
        var stats = h.SelectMany(x => new[] { (x.Winner, true), (x.Loser, false) })
            .GroupBy(t => t.Item1)
            .Select(g => (Name: g.Key, W: g.Count(t => t.Item2), L: g.Count(t => !t.Item2)))
            .OrderByDescending(t => t.W).ThenBy(t => t.L).Take(10).ToList();
        if (stats.Count > 0)
        {
            ImGui.TextColored(Pink, "Top players");
            foreach (var st in stats) ImGui.Text($"  {st.Name}: {st.W}W / {st.L}L");
        }
        ImGui.Separator();
        if (ImGui.BeginTable("##drhist", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY, new Vector2(0, Math.Max(120f, ImGui.GetContentRegionAvail().Y - 30f))))
        {
            ImGui.TableSetupColumn("When", ImGuiTableColumnFlags.WidthFixed, 110);
            ImGui.TableSetupColumn("Winner", ImGuiTableColumnFlags.WidthStretch, 0);
            ImGui.TableSetupColumn("Loser", ImGuiTableColumnFlags.WidthStretch, 0);
            ImGui.TableSetupColumn("Wager", ImGuiTableColumnFlags.WidthFixed, 90);
            ImGui.TableSetupColumn("Rolls", ImGuiTableColumnFlags.WidthFixed, 45);
            ImGui.TableHeadersRow();
            foreach (var x in h)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.TextColored(Grey, x.When.ToString("MM/dd h:mm tt"));
                ImGui.TableNextColumn(); ImGui.TextColored(Green, x.Winner);
                ImGui.TableNextColumn(); ImGui.TextColored(Red, x.Loser);
                ImGui.TableNextColumn(); ImGui.Text(x.Wager.ToString("N0"));
                ImGui.TableNextColumn(); ImGui.Text(x.Rolls.ToString());
            }
            ImGui.EndTable();
        }
        if (ImGui.Button("Clear history") && ImGui.GetIO().KeyCtrl) { h.Clear(); plugin.Save(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Hold Ctrl and click");
    }

    private void DrawSettings()
    {
        var each = Config.AnnounceEachRoll;
        if (ImGui.Checkbox("Shout every roll and whose turn is next", ref each)) { Config.AnnounceEachRoll = each; plugin.Save(); }
        var remind = Config.RemindWrongRange;
        if (ImGui.Checkbox("Shout a reminder when someone rolls the wrong number", ref remind)) { Config.RemindWrongRange = remind; plugin.Save(); }

        ImGui.Spacing();
        ImGui.TextColored(Grey, "Placeholders: {a} {b} {wager} {pot} {start} {name} {roll} {next} {max} {winner} {loser} {rolls}");
        var changed = false;
        changed |= Field("Start (first player set)", Config.StartTemplate, v => Config.StartTemplate = v);
        changed |= Field("Start (whoever goes first)", Config.StartAnyTemplate, v => Config.StartAnyTemplate = v);
        changed |= Field("Each roll", Config.RollTemplate, v => Config.RollTemplate = v);
        changed |= Field("Wrong number", Config.WrongRangeTemplate, v => Config.WrongRangeTemplate = v);
        changed |= Field("Someone dies", Config.LoserTemplate, v => Config.LoserTemplate = v);
        ImGui.Spacing();
        ImGui.TextColored(Pink, "Tournament");
        ImGui.TextColored(Grey, "Extra placeholders: {round} {match} {prize} {count} {bye} {pairs}");
        changed |= Field("Tournament start", Config.TStartTemplate, v => Config.TStartTemplate = v);
        changed |= Field("Round bracket", Config.TRoundTemplate, v => Config.TRoundTemplate = v);
        changed |= Field("Bye", Config.TByeTemplate, v => Config.TByeTemplate = v);
        changed |= Field("Match start", Config.TMatchTemplate, v => Config.TMatchTemplate = v);
        changed |= Field("Match result", Config.TResultTemplate, v => Config.TResultTemplate = v);
        changed |= Field("Champion", Config.TChampionTemplate, v => Config.TChampionTemplate = v);
        if (changed) plugin.Save();
    }

    private static bool Field(string label, string value, Action<string> set)
    {
        ImGui.Text(label);
        ImGui.SetNextItemWidth(-1);
        var v = value;
        if (!ImGui.InputText("##" + label, ref v, 300)) return false;
        set(v);
        return true;
    }
}
