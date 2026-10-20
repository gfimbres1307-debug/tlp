using System;
using System.Collections.Generic;

namespace TLP.Games.Trivia;

[Serializable]
public class TriviaQuestion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public bool Used { get; set; }
}

[Serializable]
public class PlayerScore
{
    public string Name { get; set; } = string.Empty;
    public string World { get; set; } = string.Empty;
    public int Points { get; set; }
    public long GilOwed { get; set; }
    public long GilPaid { get; set; }

    public string Display => string.IsNullOrEmpty(World) ? Name : $"{Name} ({World})";
    public long Unpaid => Math.Max(0, GilOwed - GilPaid);
}

[Serializable]
public class TriviaConfig
{
    // Question bank and tonight's scoreboard (key = "Name@World")
    public List<TriviaQuestion> Questions { get; set; } = new();
    public Dictionary<string, PlayerScore> Scores { get; set; } = new();
    public int QuestionNumber { get; set; }

    // Prizes
    public int GilPerAnswer { get; set; } = 100_000;
    public int GrandPrize { get; set; } = 5_000_000;

    // Which chat channels count as answers
    public bool ListenSay { get; set; } = true;
    public bool ListenShout { get; set; } = true;
    public bool ListenYell { get; set; } = true;
    public bool ListenTell { get; set; } = true;
    public bool ListenParty { get; set; } = true;
    public bool ListenAlliance { get; set; } = false;
    public bool ListenFreeCompany { get; set; } = false;

    // Answer rules
    public bool FirstAnswerOnly { get; set; } = true;
    public string AnswerPrefix { get; set; } = string.Empty;

    // Message templates. Placeholders: {n} {question} {answer} {names} {gil} {prize} {name} {points} {standings}
    public string QuestionTemplate { get; set; } = "TRIVIA #{n}: {question} -- answer in /say or send me a /tell!";
    public string CorrectTemplate { get; set; } = "{names} got it right! The answer was: {answer}. +{gil} gil!";
    public string NobodyTemplate { get; set; } = "Nobody got it! The answer was: {answer}.";
    public string StandingsTemplate { get; set; } = "TRIVIA STANDINGS: {standings}";
    public string WinnerTemplate { get; set; } = "{name} wins tonight's trivia with {points} correct answers and takes home {prize} gil! Congratulations!";

}
