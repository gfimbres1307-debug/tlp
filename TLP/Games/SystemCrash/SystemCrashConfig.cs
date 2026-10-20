using System;
using System.Collections.Generic;

namespace TLP.Games.SystemCrash;

[Serializable]
public class CrashRecord
{
    public DateTime When { get; set; }
    public string Prize { get; set; } = string.Empty;
    public string Winner { get; set; } = string.Empty;
    public int Players { get; set; }
    public int Rounds { get; set; }
}

[Serializable]
public class SystemCrashConfig
{
    public int RoundSeconds { get; set; } = 90;
    public int TieBreakSeconds { get; set; } = 30;
    public bool AutoAdvance { get; set; } = true;          // announce + start next round automatically when time is up
    public bool NoRollIsEliminated { get; set; } = true;   // survivors who don't roll in a round crash out
    public bool OnlyStandardRolls { get; set; } = true;
    public string Prize { get; set; } = string.Empty;

    // Placeholders: {prize} {secs} {round} {left} {crashed} {names} {name} {roll} {a} {b}
    public string StartTemplate { get; set; } = "SYSTEM CRASH! Prize: {prize}. Type /random to enter -- the lowest roll each round crashes out, last one standing wins! Round 1: {secs} seconds!";
    public string RoundTemplate { get; set; } = "ROUND {round} RESULTS: {crashed} {left} still online -- Round {next}: /random now, {secs} seconds!";
    public string FinalTemplate { get; set; } = "ROUND {round} RESULTS: {crashed} FINAL ROUND: {a} vs {b} -- highest roll wins! /random now, {secs} seconds!";
    public string CrashedText { get; set; } = "{name} ({roll}) CRASHED!";
    public string NoRollText { get; set; } = "{names} didn't roll and crashed!";
    public string TieTemplate { get; set; } = "SYSTEM GLITCH! Tie between {names} -- only you roll again, {secs} seconds!";
    public string WinnerTemplate { get; set; } = "{name} survives the SYSTEM CRASH with a {roll} and wins {prize}! Come see me to claim your prize!";
    public string TooFewTemplate { get; set; } = "SYSTEM CRASH cancelled -- not enough players rolled.";

    public List<CrashRecord> History { get; set; } = new();
}
