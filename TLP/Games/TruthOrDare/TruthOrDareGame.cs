using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;

namespace TLP.Games.TruthOrDare;

public class TodRoll
{
    public string Key = string.Empty;
    public string Name = string.Empty;
    public string World = string.Empty;
    public int Value;
    public DateTime Time;
}

/// <summary>Everyone /randoms for 60s; the highest roll asks the lowest roll "Truth or Dare?".
/// The host can shout a prompt from their own bank if the asker can't think of one.</summary>
public class TruthOrDareGame : IGame
{
    private readonly Plugin plugin;
    private readonly Countdown countdown;
    private TruthOrDareConfig Config => plugin.Config.TruthOrDare;
    public string Name => "Truth or Dare";

    private enum Phase { Idle, Countdown, Rolling, Result }
    private Phase phase = Phase.Idle;
    private DateTime openedAt;
    private readonly List<TodRoll> rolls = new();
    private TodRoll? high, low;
    private int ignored;
    private string lastSent = string.Empty;
    private readonly Random rng = new();
    private int lastTruth = -1, lastDare = -1;

    // bank editor
    private string newTruth = string.Empty;
    private string newDare = string.Empty;
    private string bulk = string.Empty;

    private static readonly Vector4 Green = new(0.45f, 1f, 0.55f, 1f);
    private static readonly Vector4 Gold = new(1f, 0.82f, 0.3f, 1f);
    private static readonly Vector4 Grey = new(0.65f, 0.65f, 0.7f, 1f);
    private static readonly Vector4 Pink = new(1f, 0.4f, 0.8f, 1f);

    public TruthOrDareGame(Plugin plugin)
    {
        this.plugin = plugin;
        countdown = new Countdown(plugin);
    }

    public void Dispose() { }

    // ---------------- chat / timing ----------------
    public void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        if (phase != Phase.Rolling) return;
        if (!Rolls.TryParse(type, sender, message, out var r)) return;
        if (Config.OnlyStandardRolls && r.CustomRange) { ignored++; return; }
        if (rolls.Any(x => x.Key == r.Key)) return;   // first roll only
        rolls.Add(new TodRoll { Key = r.Key, Name = r.Name, World = r.World, Value = r.Value, Time = DateTime.Now });
    }

    public void Update()
    {
        countdown.Update();
        if (phase == Phase.Rolling && (DateTime.Now - openedAt).TotalSeconds >= Config.RollSeconds) Finish();
    }

    private int SecondsLeft => Math.Max(0, Config.RollSeconds - (int)(DateTime.Now - openedAt).TotalSeconds);

    // ---------------- flow ----------------
    private void Start()
    {
        rolls.Clear(); ignored = 0; high = low = null;
        phase = Phase.Countdown;
        Say(Config.StartTemplate.Replace("{secs}", Config.RollSeconds.ToString()));
        countdown.Start(plugin.Config.CountdownGoRoll, () => { openedAt = DateTime.Now; phase = Phase.Rolling; });
    }

    /// <summary>Time's up: highest asks lowest. Ties go to whoever rolled first.</summary>
    private void Finish()
    {
        if (rolls.Count < 2)
        {
            Say(Config.TooFewTemplate);
            phase = Phase.Idle;
            return;
        }
        high = rolls.OrderByDescending(r => r.Value).ThenBy(r => r.Time).First();
        low = rolls.Where(r => r != high).OrderBy(r => r.Value).ThenBy(r => r.Time).First();
        phase = Phase.Result;
        Say(Config.ResultTemplate
            .Replace("{high}", high.Name).Replace("{hroll}", high.Value.ToString())
            .Replace("{low}", low.Name).Replace("{lroll}", low.Value.ToString()));
    }

    private void ShoutPrompt(bool truth, int index = -1)
    {
        if (low == null) return;
        var list = truth ? Config.Truths : Config.Dares;
        if (list.Count == 0) return;
        if (index < 0)
        {
            var last = truth ? lastTruth : lastDare;
            index = rng.Next(list.Count);
            if (list.Count > 1 && index == last) index = (index + 1) % list.Count;   // avoid repeats back to back
        }
        if (truth) lastTruth = index; else lastDare = index;
        var t = truth ? Config.TruthTemplate : Config.DareTemplate;
        Say(t.Replace("{prompt}", list[index]).Replace("{low}", low.Name).Replace("{high}", high?.Name ?? string.Empty));
    }

    private void Say(string text)
    {
        lastSent = Chat.Clean(text);
        plugin.Announce(text);
    }

    // ---------------- UI ----------------
    public void Draw()
    {
        if (ImGui.BeginTabBar("##todtabs"))
        {
            if (ImGui.BeginTabItem("Host")) { DrawHost(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Truths & Dares")) { DrawBank(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Settings")) { DrawSettings(); ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }
    }

    private void DrawHost()
    {
        switch (phase)
        {
            case Phase.Idle:
                ImGui.TextWrapped($"Everyone /randoms for {Config.RollSeconds} seconds. The highest roll asks the lowest roll: Truth or Dare?");
                if (ImGui.Button("Start round + shout")) Start();
                break;

            case Phase.Countdown:
                ImGui.TextColored(Gold, $"Counting down... {countdown.Showing}");
                break;

            case Phase.Rolling:
                ImGui.TextColored(Green, $"Taking rolls - {SecondsLeft}s left - {rolls.Count} rolled");
                if (ImGui.Button("End rolls now")) Finish();
                ImGui.SameLine();
                if (ImGui.Button("Cancel")) phase = Phase.Idle;
                break;

            case Phase.Result:
                if (high != null && low != null)
                {
                    ImGui.TextColored(Gold, $"{high.Name} ({high.Value}) asks {low.Name} ({low.Value})");
                    ImGui.TextColored(Grey, "Stuck for ideas? Shout one from your list:");
                    ImGui.BeginDisabled(Config.Truths.Count == 0);
                    if (ImGui.Button("Shout a random TRUTH")) ShoutPrompt(true);
                    ImGui.EndDisabled();
                    ImGui.SameLine();
                    ImGui.BeginDisabled(Config.Dares.Count == 0);
                    if (ImGui.Button("Shout a random DARE")) ShoutPrompt(false);
                    ImGui.EndDisabled();
                    ImGui.SameLine();
                    if (ImGui.Button("Next round")) Start();
                    ImGui.SameLine();
                    if (ImGui.Button("Done")) phase = Phase.Idle;

                    if (ImGui.CollapsingHeader("Pick a specific truth or dare"))
                    {
                        for (var i = 0; i < Config.Truths.Count; i++)
                        {
                            ImGui.PushID("t" + i);
                            if (ImGui.SmallButton("Truth")) ShoutPrompt(true, i);
                            ImGui.SameLine(); ImGui.TextWrapped(Config.Truths[i]);
                            ImGui.PopID();
                        }
                        ImGui.Separator();
                        for (var i = 0; i < Config.Dares.Count; i++)
                        {
                            ImGui.PushID("d" + i);
                            if (ImGui.SmallButton("Dare")) ShoutPrompt(false, i);
                            ImGui.SameLine(); ImGui.TextWrapped(Config.Dares[i]);
                            ImGui.PopID();
                        }
                    }
                }
                break;
        }

        if (phase == Phase.Rolling || phase == Phase.Result)
        {
            if (ignored > 0) ImGui.TextColored(Grey, $"{ignored} custom-range roll(s) ignored.");
            if (ImGui.BeginTable("##todrolls", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY, new Vector2(0, 200)))
            {
                ImGui.TableSetupColumn("Player", ImGuiTableColumnFlags.WidthStretch, 0);
                ImGui.TableSetupColumn("Roll", ImGuiTableColumnFlags.WidthFixed, 60);
                ImGui.TableHeadersRow();
                var hi = rolls.Count > 0 ? rolls.Max(r => r.Value) : -1;
                var lo = rolls.Count > 0 ? rolls.Min(r => r.Value) : -1;
                foreach (var r in rolls.OrderByDescending(r => r.Value))
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    if (r.Value == hi) ImGui.TextColored(Gold, r.Name);
                    else if (r.Value == lo) ImGui.TextColored(Pink, r.Name);
                    else ImGui.Text(r.Name);
                    ImGui.TableNextColumn(); ImGui.Text(r.Value.ToString());
                }
                ImGui.EndTable();
            }
        }

        if (!string.IsNullOrEmpty(lastSent))
        {
            ImGui.Spacing();
            ImGui.TextColored(Grey, "Last message sent: " + lastSent);
        }
    }

    private void DrawBank()
    {
        DrawList("Truths", Config.Truths, ref newTruth);
        ImGui.Separator();
        DrawList("Dares", Config.Dares, ref newDare);
        ImGui.Separator();
        ImGui.Text("Bulk add - one per line, start with T: or D:   e.g.  T: Who's your crush?");
        ImGui.InputTextMultiline("##todbulk", ref bulk, 20000, new Vector2(-1, 70));
        if (ImGui.Button("Add lines"))
        {
            foreach (var raw in bulk.Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("T:", StringComparison.OrdinalIgnoreCase) && line.Length > 2) Config.Truths.Add(line[2..].Trim());
                else if (line.StartsWith("D:", StringComparison.OrdinalIgnoreCase) && line.Length > 2) Config.Dares.Add(line[2..].Trim());
            }
            bulk = string.Empty;
            plugin.Save();
        }
    }

    private void DrawList(string title, List<string> list, ref string input)
    {
        ImGui.Text($"{title} ({list.Count})");
        ImGui.SetNextItemWidth(-70);
        ImGui.InputText("##add" + title, ref input, 300);
        ImGui.SameLine();
        if (ImGui.Button("Add##" + title) && input.Trim().Length > 0) { list.Add(input.Trim()); input = string.Empty; plugin.Save(); }

        if (ImGui.BeginTable("##list" + title, 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY, new Vector2(0, 150)))
        {
            ImGui.TableSetupColumn("Text", ImGuiTableColumnFlags.WidthStretch, 0);
            ImGui.TableSetupColumn("##del", ImGuiTableColumnFlags.WidthFixed, 40);
            var remove = -1;
            for (var i = 0; i < list.Count; i++)
            {
                ImGui.PushID(title + i);
                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.TextWrapped(list[i]);
                ImGui.TableNextColumn();
                if (ImGui.SmallButton("Del") && ImGui.GetIO().KeyCtrl) remove = i;
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Hold Ctrl and click to delete");
                ImGui.PopID();
            }
            ImGui.EndTable();
            if (remove >= 0) { list.RemoveAt(remove); plugin.Save(); }
        }
    }

    private void DrawSettings()
    {
        var changed = false;
        var s = Config.RollSeconds;
        ImGui.SetNextItemWidth(100);
        if (ImGui.InputInt("Seconds to roll", ref s, 5)) { Config.RollSeconds = Math.Max(10, s); changed = true; }
        var std = Config.OnlyStandardRolls;
        if (ImGui.Checkbox("Only count normal /random rolls (ignore /random 10 etc.)", ref std)) { Config.OnlyStandardRolls = std; changed = true; }

        ImGui.Separator();
        ImGui.Text("Messages");
        ImGui.TextColored(Grey, "Placeholders: {secs} {high} {hroll} {low} {lroll} {prompt}");
        changed |= Field("Start", () => Config.StartTemplate, v => Config.StartTemplate = v);
        changed |= Field("Result", () => Config.ResultTemplate, v => Config.ResultTemplate = v);
        changed |= Field("Truth", () => Config.TruthTemplate, v => Config.TruthTemplate = v);
        changed |= Field("Dare", () => Config.DareTemplate, v => Config.DareTemplate = v);
        changed |= Field("Too few", () => Config.TooFewTemplate, v => Config.TooFewTemplate = v);
        if (changed) plugin.Save();
    }

    private static bool Field(string label, Func<string> get, Action<string> set)
    {
        var v = get();
        ImGui.SetNextItemWidth(-90);
        if (!ImGui.InputText(label, ref v, 400)) return false;
        set(v); return true;
    }
}
