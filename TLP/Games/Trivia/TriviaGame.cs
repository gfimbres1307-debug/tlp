using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using TLP.Games;

namespace TLP.Games.Trivia;

public class SubmittedAnswer
{
    public string Key = string.Empty;
    public string Name = string.Empty;
    public string World = string.Empty;
    public string Text = string.Empty;
    public string Channel = string.Empty;
    public DateTime Time;
    public int Attempts = 1;
    public bool Selected;
    public bool LooksRight;
}

public class TriviaGame : IGame
{
    private readonly Plugin plugin;
    private TriviaConfig Config => plugin.Config.Trivia;
    private readonly TriviaUi ui;
    private readonly Countdown countdown;
    public bool CountingDown => countdown.Running;
    public int CountdownShowing => countdown.Showing;
    public string Name => "Trivia";

    public TriviaQuestion? Current { get; private set; }
    public bool Open { get; private set; }
    public DateTime OpenedAt { get; private set; }
    public int Number { get; private set; }
    public readonly List<SubmittedAnswer> Answers = new();
    public string LastSent { get; private set; } = string.Empty;

    public TriviaGame(Plugin plugin)
    {
        this.plugin = plugin;
        ui = new TriviaUi(plugin, this);
        countdown = new Countdown(plugin);
    }

    public void Draw() => ui.Draw();
    public void Update() => countdown.Update();
    public void Dispose() { }

    // ---------------- chat listening ----------------
    public void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        if (!Open || Current == null) return;
        if (!Listening(type)) return;

        // Other players carry a PlayerPayload; the host's own lines (and this plugin's shouts) do not.
        var player = sender.Payloads.OfType<PlayerPayload>().FirstOrDefault();
        if (player == null) return;

        var name = player.PlayerName;
        var world = string.Empty;
        try { world = player.World.Value.Name.ExtractText(); } catch { /* unknown world */ }

        var text = message.TextValue.Trim();
        if (!string.IsNullOrEmpty(Config.AnswerPrefix))
        {
            if (!text.StartsWith(Config.AnswerPrefix, StringComparison.OrdinalIgnoreCase)) return;
            text = text[Config.AnswerPrefix.Length..].Trim();
        }
        if (text.Length == 0) return;

        var key = $"{name}@{world}";
        var existing = Answers.FirstOrDefault(a => a.Key == key);
        if (existing != null)
        {
            existing.Attempts++;
            if (!Config.FirstAnswerOnly)
            {
                existing.Text = text;
                existing.Time = DateTime.Now;
                existing.LooksRight = Matcher.LooksRight(text, Current.Answer);
            }
            return;
        }

        var looks = Matcher.LooksRight(text, Current.Answer);
        Answers.Add(new SubmittedAnswer
        {
            Key = key, Name = name, World = world, Text = text, Channel = ChannelName(type),
            Time = DateTime.Now, LooksRight = looks, Selected = false,
        });
    }

    private bool Listening(XivChatType t) => t switch
    {
        XivChatType.Say => Config.ListenSay,
        XivChatType.Shout => Config.ListenShout,
        XivChatType.Yell => Config.ListenYell,
        XivChatType.TellIncoming => Config.ListenTell,
        XivChatType.Party or XivChatType.CrossParty => Config.ListenParty,
        XivChatType.Alliance => Config.ListenAlliance,
        XivChatType.FreeCompany => Config.ListenFreeCompany,
        _ => false,
    };

    private static string ChannelName(XivChatType t) => t switch
    {
        XivChatType.TellIncoming => "Tell",
        XivChatType.CrossParty => "Party",
        XivChatType.FreeCompany => "FC",
        _ => t.ToString(),
    };

    // ---------------- host actions ----------------
    public void Ask(TriviaQuestion q)
    {
        Current = q;
        Answers.Clear();
        Config.QuestionNumber++;
        Number = Config.QuestionNumber;
        Open = false;
        OpenedAt = DateTime.Now;
        plugin.Save();
        Announce(Fill(Config.QuestionTemplate, question: q.Question));
        countdown.Start(plugin.Config.CountdownGoAnswer, () => { Open = true; OpenedAt = DateTime.Now; });
    }

    public void ReshoutQuestion()
    {
        if (Current != null) Announce(Fill(Config.QuestionTemplate, question: Current.Question));
    }

    public void SetOpen(bool open) => Open = open;

    public void AwardSelected()
    {
        if (Current == null) return;
        var winners = Answers.Where(a => a.Selected).ToList();
        if (winners.Count == 0) return;

        foreach (var w in winners)
        {
            var s = GetScore(w.Key, w.Name, w.World);
            s.Points++;
            s.GilOwed += Config.GilPerAnswer;
        }

        var names = JoinNames(winners.Select(w => w.Name).ToList());
        Announce(Fill(Config.CorrectTemplate, names: names));
        FinishQuestion();
    }

    public void NobodyGotIt()
    {
        if (Current == null) return;
        Announce(Fill(Config.NobodyTemplate));
        FinishQuestion();
    }

    public void SkipQuietly() => FinishQuestion();

    private void FinishQuestion()
    {
        if (Current != null) Current.Used = true;
        countdown.Cancel();
        Open = false;
        Current = null;
        Answers.Clear();
        plugin.Save();
    }

    public PlayerScore GetScore(string key, string name, string world)
    {
        if (!Config.Scores.TryGetValue(key, out var s))
        {
            s = new PlayerScore { Name = name, World = world };
            Config.Scores[key] = s;
        }
        return s;
    }

    public List<KeyValuePair<string, PlayerScore>> Ranked() =>
        Config.Scores.OrderByDescending(kv => kv.Value.Points).ThenBy(kv => kv.Value.Name).ToList();

    public List<KeyValuePair<string, PlayerScore>> Leaders()
    {
        var r = Ranked();
        if (r.Count == 0 || r[0].Value.Points == 0) return new();
        var top = r[0].Value.Points;
        return r.Where(kv => kv.Value.Points == top).ToList();
    }

    public void ShoutStandings(int count = 5)
    {
        var r = Ranked().Where(kv => kv.Value.Points > 0).Take(count).ToList();
        if (r.Count == 0) return;
        var sb = new StringBuilder();
        for (var i = 0; i < r.Count; i++)
        {
            if (i > 0) sb.Append("  ");
            sb.Append($"{i + 1}. {r[i].Value.Name} ({r[i].Value.Points})");
        }
        Announce(Config.StandingsTemplate.Replace("{standings}", sb.ToString()));
    }

    public void DeclareWinner(string key)
    {
        if (!Config.Scores.TryGetValue(key, out var s)) return;
        s.GilOwed += Config.GrandPrize;
        plugin.Save();
        Announce(Config.WinnerTemplate
            .Replace("{name}", s.Name)
            .Replace("{points}", s.Points.ToString())
            .Replace("{prize}", Config.GrandPrize.ToString("N0")));
    }

    public void ResetNight()
    {
        Config.Scores.Clear();
        Config.QuestionNumber = 0;
        foreach (var q in Config.Questions) q.Used = false;
        Current = null; Open = false; Answers.Clear();
        plugin.Save();
    }

    // ---------------- helpers ----------------
    public string Fill(string template, string? question = null, string? names = null) => template
        .Replace("{n}", Number.ToString())
        .Replace("{question}", question ?? Current?.Question ?? string.Empty)
        .Replace("{answer}", Current?.Answer ?? string.Empty)
        .Replace("{names}", names ?? string.Empty)
        .Replace("{gil}", Config.GilPerAnswer.ToString("N0"))
        .Replace("{prize}", Config.GrandPrize.ToString("N0"));

    private static string JoinNames(List<string> n) => n.Count switch
    {
        0 => string.Empty,
        1 => n[0],
        2 => $"{n[0]} and {n[1]}",
        _ => string.Join(", ", n.Take(n.Count - 1)) + ", and " + n[^1],
    };

    public void Announce(string text)
    {
        LastSent = Chat.Clean(text);
        plugin.Announce(text);
    }
}

/// <summary>Loose answer matching, only used to pre-highlight likely correct answers for the host.</summary>
public static class Matcher
{
    public static string Normalize(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s.ToLowerInvariant())
            if (char.IsLetterOrDigit(c) || c == ' ') sb.Append(c);
        var t = " " + sb.ToString().Trim() + " ";
        foreach (var w in new[] { " the ", " a ", " an " }) t = t.Replace(w, " ");
        return string.Join(' ', t.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public static bool LooksRight(string given, string correct)
    {
        var g = Normalize(given); var c = Normalize(correct);
        if (g.Length == 0 || c.Length == 0) return false;
        if (g == c) return true;
        if (c.Length >= 4 && (g.Contains(c) || (g.Length >= 4 && c.Contains(g)))) return true;
        return Levenshtein(g, c) <= Math.Max(1, c.Length / 5);
    }

    private static int Levenshtein(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }
}
