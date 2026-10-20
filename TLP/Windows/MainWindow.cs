using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace TLP.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    public MainWindow(Plugin plugin) : base("The Last Protocol - Games##TLPMain")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(580, 460),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        var games = plugin.Games;
        if (games.Count == 0) { ImGui.Text("No games installed."); return; }
        var current = games.FirstOrDefault(g => g.Name == plugin.Config.SelectedGame) ?? games[0];

        ImGui.SetNextItemWidth(180);
        if (ImGui.BeginCombo("Game", current.Name))
        {
            foreach (var g in games)
                if (ImGui.Selectable(g.Name, g == current)) { plugin.Config.SelectedGame = g.Name; plugin.Save(); current = g; }
            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(60);
        var cmd = plugin.Config.OutputCommand;
        if (ImGui.InputText("Announce in", ref cmd, 16)) { plugin.Config.OutputCommand = cmd; plugin.Save(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Chat command for all game announcements: /sh, /y, /s, /p, /fc, /l1 ...");

        ImGui.SameLine();
        var cd = plugin.Config.CountdownEnabled;
        if (ImGui.Checkbox("3-2-1 countdown", ref cd)) { plugin.Config.CountdownEnabled = cd; plugin.Save(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Shout 3... 2... 1... GO! before rolls or answers start counting (all games).");

        ImGui.SetNextItemWidth(180);
        var gap = plugin.Config.SecondsBetweenMessages;
        if (ImGui.SliderFloat("Seconds between messages", ref gap, 1.0f, 8.0f, "%.1f s"))
        { plugin.Config.SecondsBetweenMessages = gap; plugin.Save(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Every message waits this long after the last one so the game doesn't block it.\nIf you still see \"Your message was not heard\", raise this.");
        if (plugin.Queue.BlockedCount > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1f, 0.45f, 0.45f, 1f), $"Game blocked {plugin.Queue.BlockedCount} (re-sent)");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("The game's spam limit blocked a message, so TLP waited and sent it again.\nIf this keeps climbing, raise Seconds between messages.");
        }
        if (plugin.Queue.Pending > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), $"{plugin.Queue.Pending} message(s) waiting to send");
            ImGui.SameLine();
            if (ImGui.SmallButton("Clear")) plugin.Queue.Clear();
        }

        ImGui.Separator();
        current.Draw();
    }
}
