using System;
using System.Collections.Generic;

namespace TLP.Games.Giveaway;

public enum RollRule
{
    Highest = 0,
    Lowest = 1,
    Closest = 2,
}

[Serializable]
public class GiveawayRecord
{
    public DateTime When { get; set; }
    public string Prize { get; set; } = string.Empty;
    public string Rule { get; set; } = string.Empty;
    public string Winner { get; set; } = string.Empty;
    public int Roll { get; set; }
    public int Entrants { get; set; }
}

[Serializable]
public class GiveawayConfig
{
    public RollRule Rule { get; set; } = RollRule.Highest;
    public int Target { get; set; } = 500;
    public int DurationSeconds { get; set; } = 90;     // 0 = close manually
    public bool OnlyStandardRolls { get; set; } = true; // ignore /random with a custom range (e.g. /random 10)

    // Placeholders: {prize} {rule} {timer} {name} {roll} {names} {target}
    public string StartTemplate { get; set; } = "GIVEAWAY! Prize: {prize}. Type /random now -- {rule} {timer}";
    public string HighestText { get; set; } = "highest roll wins!";
    public string LowestText { get; set; } = "lowest roll wins!";
    public string ClosestText { get; set; } = "closest to {target} wins!";
    public string TimerText { get; set; } = "You have {secs} seconds!";
    public string WinnerTemplate { get; set; } = "{name} wins {prize} with a roll of {roll}! Come see me to claim your prize!";
    public string TieTemplate { get; set; } = "TIE between {names}! Only you roll again -- type /random now!";
    public string TellTemplate { get; set; } = "Congrats, you won {prize} in the giveaway! Come find me to claim it.";

    public List<GiveawayRecord> History { get; set; } = new();
}
