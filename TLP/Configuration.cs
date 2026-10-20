using System;
using Dalamud.Configuration;
using TLP.Games.Giveaway;
using TLP.Games.SystemCrash;
using TLP.Games.TruthOrDare;
using TLP.Games.Trivia;

namespace TLP;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>Chat command every game announces with: /sh, /y, /s, /p, /fc, /l1 ...</summary>
    public string OutputCommand { get; set; } = "/sh";

    /// <summary>Name of the game shown when the window opens.</summary>
    public string SelectedGame { get; set; } = "Trivia";

    /// <summary>"3... 2... 1... GO!" before any game starts counting rolls/answers.</summary>
    public bool CountdownEnabled { get; set; } = true;
    public int CountdownSeconds { get; set; } = 3;
    public string CountdownGoRoll { get; set; } = "GO! /random now!";
    public string CountdownGoAnswer { get; set; } = "GO! Answer now!";

    public TriviaConfig Trivia { get; set; } = new();
    public GiveawayConfig Giveaway { get; set; } = new();
    public SystemCrashConfig SystemCrash { get; set; } = new();
    public TruthOrDareConfig TruthOrDare { get; set; } = new();
}
