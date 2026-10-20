using System;
using System.Collections.Generic;

namespace TLP.Games.DeathRoll;

[Serializable]
public class DeathRecord
{
    public DateTime When { get; set; }
    public string A { get; set; } = string.Empty;
    public string B { get; set; } = string.Empty;
    public string Winner { get; set; } = string.Empty;
    public string Loser { get; set; } = string.Empty;
    public long Wager { get; set; }
    public int Start { get; set; }
    public int Rolls { get; set; }
    public bool HouseInvolved { get; set; }
    public bool HouseWon { get; set; }
}

[Serializable]
public class DeathRollConfig
{
    public int StartNumber { get; set; } = 999;
    public int Wager { get; set; } = 100_000;
    public bool AnnounceEachRoll { get; set; } = true;
    public bool RemindWrongRange { get; set; } = true;

    // Placeholders: {a} {b} {wager} {pot} {start} {name} {roll} {next} {max} {winner} {loser} {rolls}
    public string StartTemplate { get; set; } = "DEATH ROLL! {a} vs {b} for {wager} gil -- starting at {start}. Roll the 1 and you DIE. {next}, you're up: /random {start}";
    public string StartAnyTemplate { get; set; } = "DEATH ROLL! {a} vs {b} for {wager} gil -- starting at {start}. Roll the 1 and you DIE. Whoever goes first: /random {start}";
    public string RollTemplate { get; set; } = "{name} rolled {roll}! {next}, /random {roll}";
    public string WrongRangeTemplate { get; set; } = "{name}, that one doesn't count -- type /random {max}";
    public string LoserTemplate { get; set; } = "{loser} rolled a 1 and DIED! {winner} wins the death roll and takes {pot} gil! ({rolls} rolls)";

    // ---- tournament ----
    public string TournamentPrize { get; set; } = "1,000,000 gil";
    public bool RandomFirstInTournament { get; set; } = true;
    // Extra placeholders: {round} {match} {prize} {count} {bye} {pairs}
    public string TStartTemplate { get; set; } = "DEATH ROLL TOURNAMENT! {count} players enter -- only one survives and wins {prize}!";
    public string TRoundTemplate { get; set; } = "ROUND {round} BRACKET: {pairs}";
    public string TByeTemplate { get; set; } = "{bye} gets a lucky bye and advances!";
    public string TMatchTemplate { get; set; } = "ROUND {round}, MATCH {match}: {a} vs {b}! {next}, you're up: /random {start}";
    public string TResultTemplate { get; set; } = "{loser} rolled a 1 and is OUT! {winner} advances!";
    public string TChampionTemplate { get; set; } = "{winner} is the last one standing and wins the DEATH ROLL TOURNAMENT -- {prize}! Come see me to claim it!";

    public List<DeathRecord> History { get; set; } = new();
}
