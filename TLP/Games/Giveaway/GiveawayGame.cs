using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;

namespace TLP.Games.Giveaway;

public class Roll
{
    public string Key = string.Empty;
    public string Name = string.Empty;
    public string World = string.Empty;
    public int Value;
    public DateTime Time;
}

public class GiveawayGame : IGame
{
    private readonly Plugin plugin;
    private GiveawayConfig Config => plugin.Config.Giveaway;
    public string Name => "Giveaway";

    // prize
    private bool capturing;
    private uint prizeItemId;
    private bool prizeHq;
    private string prizeItemName = string.Empty;
    private int prizeQty = 1;
    private string customPrize = string.Empty;
    private bool useCustom;

    // round state
    private enum Phase { Setup, Countdown, Rolling, Closed, Done }
    private readonly Countdown countdown;
    private Phase phase = Phase.Setup;
    private DateTime openedAt;
    private string runningPrize = string.Empty;
    private RollRule runningRule;
    private int runningTarget;
    private HashSet<string>? onlyThese;          // tie re-roll: only these players count
    private readonly List<Roll> rolls = new();
    private int ignoredRolls;
    private string lastSent = string.Empty;

    private static readonly Vector4 Green = new(0.45f, 1f, 0.55f, 1f);
    private static readonly Vector4 Gold = new(1f, 0.82f, 0.3f, 1f);
    private static readonly Vector4 Grey = new(0.65f, 0.65f, 0.7f, 1f);
    private static readonly Vector4 Pink = new(1f, 0.4f, 0.8f, 1f);


    public GiveawayGame(Plugin plugin)
    {
        this.plugin = plugin;
        countdown = new Countdown(plugin);
        Plugin.GameGui.HoveredItemChanged += OnHoveredItemChanged;
    }

    public void Dispose() => Plugin.GameGui.HoveredItemChanged -= OnHoveredItemChanged;

    // ---------------- prize picking ----------------
    private void OnHoveredItemChanged(object? sender, ulong id)
    {
        if (!capturing || id == 0) return;
        var hq = false;
        if (id >= 1_000_000) { id -= 1_000_000; hq = true; }
        else if (id >= 500_000) id -= 500_000;   // collectable
        var name = LookupItem((uint)id);
        if (string.IsNullOrEmpty(name)) return;
        prizeItemId = (uint)id; prizeHq = hq; prizeItemName = name; useCustom = false; capturing = false;
    }

    private static string LookupItem(uint id)
    {
        try
        {
            var row = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>().GetRowOrDefault(id);
            return row?.Name.ExtractText() ?? string.Empty;
        }
        catch { return string.Empty; }
    }

    private string PrizeText()
    {
        if (useCustom || prizeItemId == 0) return customPrize.Trim();
        var n = prizeItemName + (prizeHq ? " (HQ)" : string.Empty);
        return prizeQty > 1 ? $"{prizeQty}x {n}" : n;
    }

    // ---------------- roll reading ----------------
    public void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        if (phase != Phase.Rolling) return;
        if (!Rolls.TryParse(type, sender, message, out var r)) return;
        if (Config.OnlyStandardRolls && r.CustomRange) { ignoredRolls++; return; }
        if (onlyThese != null && !onlyThese.Contains(r.Key)) return;
        if (rolls.Any(x => x.Key == r.Key)) return;  // first roll only
        rolls.Add(new Roll { Key = r.Key, Name = r.Name, World = r.World, Value = r.Value, Time = DateTime.Now });
    }

    /// <summary>Runs every frame (even with the window closed): closes the roll window when time is up.</summary>
    public void Update()
    {
        countdown.Update();
        if (phase == Phase.Rolling && Config.DurationSeconds > 0 && (DateTime.Now - openedAt).TotalSeconds >= Config.DurationSeconds)
            phase = Phase.Closed;
    }

    // ---------------- ranking ----------------
    private int Score(Roll r) => runningRule switch
    {
        RollRule.Highest => -r.Value,
        RollRule.Lowest => r.Value,
        _ => Math.Abs(r.Value - runningTarget),
    };

    private List<Roll> Ranked() => rolls.OrderBy(Score).ThenBy(r => r.Time).ToList();

    private List<Roll> Leaders()
    {
        var r = Ranked();
        if (r.Count == 0) return r;
        var best = Score(r[0]);
        return r.Where(x => Score(x) == best).ToList();
    }

    private string RuleText(RollRule rule, int target) => (rule switch
    {
        RollRule.Highest => Config.HighestText,
        RollRule.Lowest => Config.LowestText,
        _ => Config.ClosestText,
    }).Replace("{target}", target.ToString());

    // ---------------- actions ----------------
    private void Start()
    {
        runningPrize = PrizeText();
        runningRule = Config.Rule;
        runningTarget = Math.Clamp(Config.Target, 1, 999);
        onlyThese = null;
        rolls.Clear(); ignoredRolls = 0;
        openedAt = DateTime.Now;
        phase = Phase.Countdown;
        var timer = Config.DurationSeconds > 0 ? Config.TimerText.Replace("{secs}", Config.DurationSeconds.ToString()) : string.Empty;
        Say(Config.StartTemplate
            .Replace("{prize}", runningPrize)
            .Replace("{rule}", RuleText(runningRule, runningTarget))
            .Replace("{timer}", timer)
            .Replace("{target}", runningTarget.ToString()));
        countdown.Start(plugin.Config.CountdownGoRoll, () => { openedAt = DateTime.Now; phase = Phase.Rolling; });
    }

    private void TieReroll(List<Roll> tied)
    {
        onlyThese = new HashSet<string>(tied.Select(t => t.Key));
        rolls.Clear(); ignoredRolls = 0;
        openedAt = DateTime.Now;
        phase = Phase.Countdown;
        Say(Config.TieTemplate.Replace("{names}", string.Join(", ", tied.Select(t => t.Name))));
        countdown.Start(plugin.Config.CountdownGoRoll, () => { openedAt = DateTime.Now; phase = Phase.Rolling; });
    }

    private void AnnounceWinner(Roll w)
    {
        Say(Config.WinnerTemplate
            .Replace("{name}", w.Name)
            .Replace("{prize}", runningPrize)
            .Replace("{roll}", w.Value.ToString())
            .Replace("{target}", runningTarget.ToString()));
        Config.History.Insert(0, new GiveawayRecord
        {
            When = DateTime.Now, Prize = runningPrize, Rule = RuleText(runningRule, runningTarget),
            Winner = string.IsNullOrEmpty(w.World) ? w.Name : $"{w.Name} ({w.World})", Roll = w.Value, Entrants = rolls.Count,
        });
        if (Config.History.Count > 200) Config.History.RemoveRange(200, Config.History.Count - 200);
        plugin.Save();
        phase = Phase.Done;
        winnerAnnounced = w;
    }

    private Roll? winnerAnnounced;

    private void TellWinner(Roll w)
    {
        var target = string.IsNullOrEmpty(w.World) ? w.Name : $"{w.Name}@{w.World}";
        var msg = Config.TellTemplate.Replace("{prize}", runningPrize).Replace("{name}", w.Name);
        lastSent = Chat.Clean(msg);
        Chat.Send($"/tell {target} {msg}");
    }

    private void Say(string text)
    {
        lastSent = Chat.Clean(text);
        plugin.Announce(text);
    }

    // ---------------- UI ----------------
    public void Draw()
    {
        if (ImGui.BeginTabBar("##givetabs"))
        {
            if (ImGui.BeginTabItem("Host")) { DrawHost(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("History")) { DrawHistory(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Settings")) { DrawSettings(); ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }
    }

    private void DrawHost()
    {
        if (phase == Phase.Setup || phase == Phase.Done) DrawSetup();
        else DrawRound();

        if (!string.IsNullOrEmpty(lastSent))
        {
            ImGui.Spacing();
            ImGui.TextColored(Grey, "Last message sent: " + lastSent);
        }
    }

    private void DrawSetup()
    {
        if (phase == Phase.Done && winnerAnnounced != null)
        {
            ImGui.TextColored(Green, $"Last winner: {winnerAnnounced.Name} - {runningPrize}");
            if (ImGui.Button("Send them a /tell")) TellWinner(winnerAnnounced);
            ImGui.Separator();
        }

        ImGui.TextColored(Pink, "1. Prize");
        if (capturing)
        {
            ImGui.TextColored(Gold, "Hover over the prize in your inventory now...");
            if (ImGui.Button("Cancel")) capturing = false;
        }
        else if (ImGui.Button("Pick prize from inventory")) capturing = true;
        if (ImGui.IsItemHovered() && !capturing) ImGui.SetTooltip("Click, then hover your mouse over the item in your inventory.");

        if (!useCustom && prizeItemId != 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(Gold, prizeItemName + (prizeHq ? " (HQ)" : string.Empty));
            ImGui.SetNextItemWidth(100);
            if (ImGui.InputInt("Quantity", ref prizeQty, 1)) prizeQty = Math.Max(1, prizeQty);
        }
        ImGui.SetNextItemWidth(260);
        if (ImGui.InputText("Or type a prize (e.g. 1,000,000 gil)", ref customPrize, 120)) useCustom = customPrize.Trim().Length > 0;

        ImGui.Spacing();
        ImGui.TextColored(Pink, "2. How to win");
        var rule = (int)Config.Rule;
        var changed = false;
        if (ImGui.RadioButton("Highest roll", ref rule, 0)) changed = true;
        ImGui.SameLine();
        if (ImGui.RadioButton("Lowest roll", ref rule, 1)) changed = true;
        ImGui.SameLine();
        if (ImGui.RadioButton("Closest to", ref rule, 2)) changed = true;
        if (changed) { Config.Rule = (RollRule)rule; plugin.Save(); }
        if (Config.Rule == RollRule.Closest)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            var t = Config.Target;
            if (ImGui.InputInt("##target", ref t, 1)) { Config.Target = Math.Clamp(t, 1, 999); plugin.Save(); }
        }

        ImGui.SetNextItemWidth(100);
        var d = Config.DurationSeconds;
        if (ImGui.InputInt("Seconds to roll (0 = I'll close it)", ref d, 5)) { Config.DurationSeconds = Math.Max(0, d); plugin.Save(); }

        ImGui.Spacing();
        var prize = PrizeText();
        ImGui.BeginDisabled(prize.Length == 0);
        if (ImGui.Button("3. Start giveaway + shout")) Start();
        ImGui.EndDisabled();
        if (prize.Length == 0) { ImGui.SameLine(); ImGui.TextColored(Grey, "Pick or type a prize first."); }
        else
        {
            var timer = Config.DurationSeconds > 0 ? Config.TimerText.Replace("{secs}", Config.DurationSeconds.ToString()) : string.Empty;
            ImGui.TextColored(Grey, "Will shout: " + Chat.Clean(Config.StartTemplate.Replace("{prize}", prize)
                .Replace("{rule}", RuleText(Config.Rule, Config.Target)).Replace("{timer}", timer).Replace("{target}", Config.Target.ToString())));
        }
    }

    private void DrawRound()
    {
        ImGui.TextColored(Pink, $"Prize: {runningPrize}");
        ImGui.Text($"Rule: {RuleText(runningRule, runningTarget)}" + (onlyThese != null ? "  (tie re-roll)" : string.Empty));

        if (phase == Phase.Countdown)
        {
            ImGui.TextColored(Gold, $"Counting down... {countdown.Showing}");
            return;
        }
        var secs = (int)(DateTime.Now - openedAt).TotalSeconds;
        if (phase == Phase.Rolling)
        {
            var left = Config.DurationSeconds > 0 ? $"  -  {Math.Max(0, Config.DurationSeconds - secs)}s left" : string.Empty;
            ImGui.TextColored(Green, $"Taking rolls... {rolls.Count} so far{left}");
            if (ImGui.Button("Close rolls")) phase = Phase.Closed;
            ImGui.SameLine();
            if (ImGui.Button("Re-shout instructions"))
                Say(Config.StartTemplate.Replace("{prize}", runningPrize).Replace("{rule}", RuleText(runningRule, runningTarget)).Replace("{timer}", string.Empty).Replace("{target}", runningTarget.ToString()));
        }
        else
        {
            ImGui.TextColored(Grey, $"Rolls closed - {rolls.Count} entries.");
            ImGui.SameLine();
            if (ImGui.SmallButton("Reopen")) phase = Phase.Rolling;
        }
        if (ignoredRolls > 0) ImGui.TextColored(Grey, $"{ignoredRolls} roll(s) ignored for using a custom range (e.g. /random 10).");

        if (ImGui.BeginTable("##rolls", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY, new Vector2(0, 230)))
        {
            ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 25);
            ImGui.TableSetupColumn("Player", ImGuiTableColumnFlags.WidthStretch, 0);
            ImGui.TableSetupColumn("Roll", ImGuiTableColumnFlags.WidthFixed, 50);
            ImGui.TableSetupColumn(runningRule == RollRule.Closest ? "Off by" : "", ImGuiTableColumnFlags.WidthFixed, 50);
            ImGui.TableHeadersRow();
            var ranked = Ranked();
            var leaders = Leaders();
            for (var i = 0; i < ranked.Count; i++)
            {
                var r = ranked[i];
                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.Text((i + 1).ToString());
                ImGui.TableNextColumn();
                if (leaders.Contains(r)) ImGui.TextColored(Gold, r.Name); else ImGui.Text(r.Name);
                if (!string.IsNullOrEmpty(r.World)) { ImGui.SameLine(); ImGui.TextColored(Grey, r.World); }
                ImGui.TableNextColumn(); ImGui.Text(r.Value.ToString());
                ImGui.TableNextColumn(); if (runningRule == RollRule.Closest) ImGui.Text(Math.Abs(r.Value - runningTarget).ToString());
            }
            ImGui.EndTable();
        }

        if (phase != Phase.Closed) return;

        var lead = Leaders();
        if (lead.Count == 0)
        {
            ImGui.TextColored(Grey, "Nobody rolled.");
            if (ImGui.Button("Cancel giveaway")) phase = Phase.Setup;
            return;
        }
        if (lead.Count == 1)
        {
            ImGui.TextColored(Green, $"Winner: {lead[0].Name} with {lead[0].Value}");
            if (ImGui.Button($"Announce {lead[0].Name} + shout")) AnnounceWinner(lead[0]);
        }
        else
        {
            ImGui.TextColored(Gold, $"Tie between {string.Join(", ", lead.Select(l => l.Name))}!");
            if (ImGui.Button("Shout tie re-roll (only they roll)")) TieReroll(lead);
            ImGui.TextColored(Grey, "Or pick a winner yourself:");
            foreach (var l in lead)
            {
                ImGui.PushID("pick" + l.Key);
                if (ImGui.Button($"Announce {l.Name}")) AnnounceWinner(l);
                ImGui.PopID();
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel giveaway")) phase = Phase.Setup;
    }

    private void DrawHistory()
    {
        if (Config.History.Count == 0) { ImGui.TextColored(Grey, "No giveaways yet."); return; }
        if (ImGui.BeginTable("##hist", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY, new Vector2(0, 320)))
        {
            ImGui.TableSetupColumn("When", ImGuiTableColumnFlags.WidthFixed, 110);
            ImGui.TableSetupColumn("Prize", ImGuiTableColumnFlags.WidthStretch, 0);
            ImGui.TableSetupColumn("Winner", ImGuiTableColumnFlags.WidthFixed, 150);
            ImGui.TableSetupColumn("Roll", ImGuiTableColumnFlags.WidthFixed, 40);
            ImGui.TableSetupColumn("Entrants", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableHeadersRow();
            foreach (var h in Config.History)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.Text(h.When.ToString("MM/dd HH:mm"));
                ImGui.TableNextColumn(); ImGui.TextWrapped(h.Prize);
                ImGui.TableNextColumn(); ImGui.Text(h.Winner);
                ImGui.TableNextColumn(); ImGui.Text(h.Roll.ToString());
                ImGui.TableNextColumn(); ImGui.Text(h.Entrants.ToString());
            }
            ImGui.EndTable();
        }
        if (ImGui.Button("Clear history") && ImGui.GetIO().KeyCtrl) { Config.History.Clear(); plugin.Save(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Hold Ctrl and click");
    }

    private void DrawSettings()
    {
        var changed = false;
        var std = Config.OnlyStandardRolls;
        if (ImGui.Checkbox("Only count normal /random rolls (ignore /random 10 etc.)", ref std)) { Config.OnlyStandardRolls = std; changed = true; }

        ImGui.Separator();
        ImGui.Text("Messages");
        ImGui.TextColored(Grey, "Placeholders: {prize} {rule} {timer} {target} | winner: {name} {roll} | tie: {names} | timer: {secs}");
        changed |= Field("Start", () => Config.StartTemplate, v => Config.StartTemplate = v);
        changed |= Field("Highest rule", () => Config.HighestText, v => Config.HighestText = v);
        changed |= Field("Lowest rule", () => Config.LowestText, v => Config.LowestText = v);
        changed |= Field("Closest rule", () => Config.ClosestText, v => Config.ClosestText = v);
        changed |= Field("Timer", () => Config.TimerText, v => Config.TimerText = v);
        changed |= Field("Winner", () => Config.WinnerTemplate, v => Config.WinnerTemplate = v);
        changed |= Field("Tie", () => Config.TieTemplate, v => Config.TieTemplate = v);
        changed |= Field("Tell to winner", () => Config.TellTemplate, v => Config.TellTemplate = v);
        if (changed) plugin.Save();
    }

    private static bool Field(string label, Func<string> get, Action<string> set)
    {
        var v = get();
        ImGui.SetNextItemWidth(-110);
        if (!ImGui.InputText(label, ref v, 400)) return false;
        set(v); return true;
    }
}
