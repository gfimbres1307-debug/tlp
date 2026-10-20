using System;
using System.Collections.Generic;

namespace TLP.Games.TruthOrDare;

[Serializable]
public class TruthOrDareConfig
{
    public int RollSeconds { get; set; } = 60;
    public bool OnlyStandardRolls { get; set; } = true;

    // Placeholders: {secs} {high} {hroll} {low} {lroll} {prompt}
    public string StartTemplate { get; set; } = "TRUTH OR DARE! Type /random to play -- the highest roll gets to ask the lowest roll! {secs} seconds!";
    public string ResultTemplate { get; set; } = "{high} rolled highest ({hroll}) and gets to ask {low} ({lroll}): TRUTH or DARE?";
    public string TruthTemplate { get; set; } = "TRUTH for {low}: {prompt}";
    public string DareTemplate { get; set; } = "DARE for {low}: {prompt}";
    public string TooFewTemplate { get; set; } = "Truth or Dare needs at least two players -- nobody to ask!";

    public List<string> Truths { get; set; } = new()
    {
        "Who in this venue would you most want to take on a date tonight?",
        "What's your biggest turn-on in a partner?",
        "Who here would you kiss right now if you had to pick?",
        "What's the most scandalous thing you've ever done in game?",
        "Have you ever had a crush on someone in this room? Who?",
        "What's the wildest place you've ever kissed someone?",
        "What's the naughtiest thing in your search history?",
        "Rate everyone in the party from 1 to 10 on hotness.",
        "What's your most embarrassing hookup story?",
        "Which glamour makes you feel the sexiest?",
        "Whose DMs would you least want anyone to read -- and why?",
        "What's the boldest pick-up line you've ever actually used?",
        "What's a secret you've never told anyone here?",
        "Describe your ideal makeout spot in Eorzea.",
        "Who was the last person you flirted with in game?",
        "What's the most romantic thing someone's ever done for you?",
        "If you had to marry someone at this bar, who would it be?",
        "What's your guilty-pleasure fantasy date?",
        "What's the most jealous you've ever gotten over someone?",
        "What's one thing about you that would surprise everyone here?",
        "What's the longest you've gone without a kiss?",
        "Who here has the best voice/emotes -- and does it do something for you?",
    };

    public List<string> Dares { get; set; } = new()
    {
        "/blowkiss the person who asked you, then say something sweet in /say.",
        "Flirt with the bartender in /say for the next 60 seconds.",
        "Send your most seductive /tell to someone the asker picks.",
        "Strut a full lap around the bar in your sexiest glamour.",
        "Shout your best pick-up line in /shout.",
        "Let the asker pick one piece of your glamour for the rest of the night.",
        "/embrace the person to your left and don't let go for 10 seconds.",
        "Strip down to your smallclothes until your next turn.",
        "Describe your crush in this room without saying their name.",
        "Serenade someone with a love song in /say (at least three lines).",
        "Dance on top of the bar until someone tips you.",
        "Give the person who asked you a lap-dance emote of your choice.",
        "Read the last /tell you sent out loud in /say.",
        "Swap one glamour piece with the asker for the rest of the night.",
        "Let the group choose your title/nickname for the rest of the night.",
        "Tip the bartender 69,069 gil.",
        "Give everyone at the bar a compliment, one flirtier than the last.",
        "Propose to someone of the asker's choice -- on one knee.",
        "Say the cheesiest thing you can think of to the person who rolled highest.",
        "Do your most seductive /dance in front of the asker.",
        "Whisper (/tell) a secret to the asker that you'd never say in /say.",
        "Change your glamour to whatever the asker describes, within one minute.",
    };
}
