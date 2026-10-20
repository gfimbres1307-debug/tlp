using System;
using System.Linq;
using System.Text.RegularExpressions;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;

namespace TLP;

public readonly record struct RollInfo(string Key, string Name, string World, int Value, bool CustomRange);

/// <summary>Reads the game's "/random" result lines (English client).</summary>
public static class Rolls
{
    // "Random! Remy Kweli rolls a 512."   "Random! (1-100) Remy Kweli rolls a 54."
    private static readonly Regex RollRx = new(@"^Random!\s*(?:\((\d+)\s*-\s*(\d+)\)\s*)?(.+?)\s+rolls?\s+an?\s+(\d+)", RegexOptions.Compiled);

    /// <summary>Player chat channels. A "Random!" line in one of these was typed by a person, not rolled.</summary>
    private static bool IsPlayerChat(XivChatType t)
    {
        var c = (int)t & 0x7F;
        return c is 10 or 11 or 12 or 13 or 14 or 15 or (>= 16 and <= 23) or 24 or 27 or 28 or 29 or 30 or 32 or 37 or (>= 101 and <= 107);
    }

    /// <summary>True if this chat line is someone else's /random. The host's own rolls ("You roll") are skipped.</summary>
    public static bool TryParse(XivChatType type, SeString sender, SeString message, out RollInfo roll)
    {
        roll = default;
        if (IsPlayerChat(type)) return false;
        var text = message.TextValue.Trim();
        if (!text.StartsWith("Random!", StringComparison.Ordinal)) return false;
        var m = RollRx.Match(text);
        if (!m.Success) return false;

        var who = m.Groups[3].Value.Trim();
        if (who.Equals("You", StringComparison.OrdinalIgnoreCase)) return false;
        if (!int.TryParse(m.Groups[4].Value, out var value)) return false;
        var custom = m.Groups[2].Success && m.Groups[2].Value != "999";

        var name = who; var world = string.Empty;
        var pp = message.Payloads.OfType<PlayerPayload>().FirstOrDefault() ?? sender.Payloads.OfType<PlayerPayload>().FirstOrDefault();
        if (pp != null)
        {
            name = pp.PlayerName;
            try { world = pp.World.Value.Name.ExtractText(); } catch { /* unknown world */ }
        }
        roll = new RollInfo($"{name}@{world}", name, world, value, custom);
        return true;
    }
}
