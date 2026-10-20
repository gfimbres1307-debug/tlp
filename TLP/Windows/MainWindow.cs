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

        ImGui.Separator();
        current.Draw();
    }
}
