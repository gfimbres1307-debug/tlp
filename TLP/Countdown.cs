using System;

namespace TLP;

/// <summary>
/// Queues "3... 2... 1... GO!" through the chat queue (so it is spaced out like every other message),
/// then runs <c>onGo</c> the moment GO is actually sent. Games ignore rolls/answers until then.
/// </summary>
public class Countdown
{
    private readonly Plugin plugin;
    private int step = -1;   // number currently on screen; -1 = not running

    public Countdown(Plugin plugin) => this.plugin = plugin;

    public bool Running => step >= 0;
    public int Showing => Math.Max(0, step);

    /// <summary>Starts the countdown, or runs onGo straight away if countdowns are switched off.</summary>
    public void Start(string goText, Action onGo)
    {
        plugin.Queue.Remove(this);
        if (!plugin.Config.CountdownEnabled || plugin.Config.CountdownSeconds <= 0)
        {
            step = -1;
            onGo();
            return;
        }
        var n = plugin.Config.CountdownSeconds;
        step = n;
        for (var i = n; i >= 1; i--)
        {
            var shown = i;
            plugin.Announce($"{i}...", () => step = shown, this);
        }
        void Go() { step = -1; onGo(); }
        if (string.IsNullOrWhiteSpace(goText))
            plugin.Queue.Enqueue(string.Empty, Go, this);   // empty line is skipped by Chat.Send
        else
            plugin.Announce(goText, Go, this);
    }

    public void Cancel() { plugin.Queue.Remove(this); step = -1; }

    /// <summary>Kept for the games' frame loop; the chat queue does the timing now.</summary>
    public void Update() { }
}
