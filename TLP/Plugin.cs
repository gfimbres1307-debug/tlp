using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Chat;
using Dalamud.Game.Command;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using TLP.Games;
using TLP.Games.Giveaway;
using TLP.Games.SystemCrash;
using TLP.Games.TruthOrDare;
using TLP.Games.Trivia;
using TLP.Windows;

namespace TLP;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IContextMenu ContextMenu { get; private set; } = null!;

    private const string CommandName = "/tlp";

    public Configuration Config { get; }
    public ChatQueue Queue { get; }
    public List<IGame> Games { get; } = new();

    public readonly WindowSystem WindowSystem = new("TLP");
    private readonly MainWindow mainWindow;

    public Plugin()
    {
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Queue = new ChatQueue(this);

        // ---- register games here ----
        Games.Add(new TriviaGame(this));
        Games.Add(new GiveawayGame(this));
        Games.Add(new SystemCrashGame(this));
        Games.Add(new TruthOrDareGame(this));

        mainWindow = new MainWindow(this);
        WindowSystem.AddWindow(mainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the TLP games window. /tlp <game name> opens straight to that game, e.g. /tlp trivia",
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainWindow;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleMainWindow;

        ChatGui.ChatMessage += OnChatMessage;
        Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        ChatGui.ChatMessage -= OnChatMessage;
        Framework.Update -= OnFrameworkUpdate;

        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainWindow;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleMainWindow;

        WindowSystem.RemoveAllWindows();
        foreach (var g in Games) g.Dispose();

        CommandManager.RemoveHandler(CommandName);
    }

    private void OnChatMessage(IHandleableChatMessage msg)
    {
        var type = msg.LogKind;
        var sender = msg.Sender;
        var message = msg.Message;
        if (!Rolls.IsPlayerChat(type) && message.TextValue.StartsWith("Your message was not heard", System.StringComparison.OrdinalIgnoreCase))
        {
            Queue.OnBlocked();
            return;
        }
        foreach (var g in Games)
        {
            try { g.OnChatMessage(type, sender, message); }
            catch (System.Exception ex) { Log.Error(ex, $"{g.Name} failed to read a chat message"); }
        }
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        try { Queue.Update(); }
        catch (System.Exception ex) { Log.Error(ex, "Chat queue failed"); }
        foreach (var g in Games)
        {
            try { g.Update(); }
            catch (System.Exception ex) { Log.Error(ex, $"{g.Name} update failed"); }
        }
    }

    private void OnCommand(string command, string args)
    {
        var wanted = args.Trim();
        if (wanted.Length > 0)
        {
            var g = Games.FirstOrDefault(x => x.Name.StartsWith(wanted, System.StringComparison.OrdinalIgnoreCase));
            if (g != null) { Config.SelectedGame = g.Name; Save(); mainWindow.IsOpen = true; return; }
        }
        ToggleMainWindow();
    }

    public void ToggleMainWindow() => mainWindow.Toggle();

    /// <summary>Opens the window on a specific game (used by the inventory right-click menu).</summary>
    public void ShowGame(string name) { Config.SelectedGame = name; Save(); mainWindow.IsOpen = true; }

    public void Save() => PluginInterface.SavePluginConfig(Config);

    /// <summary>Send an announcement in the configured channel. Only called from host button clicks.</summary>
    public void Announce(string text, System.Action? after = null, object? owner = null)
    {
        var cmd = string.IsNullOrWhiteSpace(Config.OutputCommand) ? "/sh" : Config.OutputCommand.Trim();
        Queue.Enqueue($"{cmd} {text}", after, owner);
    }

    /// <summary>Queue any raw chat line (e.g. a /tell) so it is spaced out like announcements.</summary>
    public void SendLine(string line) => Queue.Enqueue(line);
}
