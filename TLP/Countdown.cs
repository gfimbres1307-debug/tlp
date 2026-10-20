using System;

namespace TLP;

/// <summary>
/// Shouts "3... 2... 1... GO!" one second apart, then runs <c>onGo</c>.
/// Games ignore rolls/answers until GO. Driven by the game's Update() each frame.
/// </summary>
public class Countdown
{
    private readonly Plugin plugin;
    private int step = -1;
    private DateTime next;
    private Action? onGo;
    private string goText = string.Empty;

    public Countdown(Plugin plugin) => this.plugin = plugin;

    public bool Running => step >= 0;
    public int Showing => Math.Max(0, step);

    /// <summary>Starts the countdown, or runs onGo straight away if countdowns are switched off.</summary>
    public void Start(string go, Action whenGo)
    {
        if (!plugin.Config.CountdownEnabled || plugin.Config.CountdownSeconds <= 0)
        {
            step = -1;
            whenGo();
            return;
        }
        goText = go;
        onGo = whenGo;
        step = plugin.Config.CountdownSeconds;
        next = DateTime.Now.AddSeconds(1);   // a beat after the opening message
    }

    public void Cancel() { step = -1; onGo = null; }

    public void Update()
    {
        if (step < 0 || DateTime.Now < next) return;
        if (step > 0)
        {
            plugin.Announce($"{step}...");
            step--;
            next = DateTime.Now.AddSeconds(1);
            return;
        }
        step = -1;
        if (!string.IsNullOrWhiteSpace(goText)) plugin.Announce(goText);
        var go = onGo; onGo = null;
        go?.Invoke();
    }
}
