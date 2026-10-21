using System;
using System.Linq;
using System.Text.RegularExpressions;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;

namespace TLP;

/// <param name="Max">Top of the roll range shown in the line (e.g. 523 for "(1-523)"), or 999 when no range is shown.</param>
/// <param name="IsSelf">True when the host rolled ("You roll...").</param>
public readonly record struct RollInfo(string Key, string Name, string World, int Value, bool CustomRange, int Max = 999, bool IsSelf = false);

/// <summary>Reads the game's "/random" result lines (English client).</summary>
public static class Rolls
{
    // Current client:  "Random! Loraza Kweli rolls a 691 (out of 999)."   "Random! You roll a 54 (out of 100)."
    // Older format:    "Random! (1-100) Remy Kweli rolls a 54."
    // A dice icon and the player's world icon can sit inside the line, so anything non-digit is allowed before the number.
    private static readonly Regex RollRx = new(@"^Random!\s*(?:\((\d+)\s*-\s*(\d+)\)\s*)?(.+?)\s+rolls?\s+an?\D*?(\d+)(?:\D*?\(out of\s*(\d+)\))?", RegexOptions.Compiled);

    /// <summary>Player chat channels. A "Random!" line in one of these was typed by a person, not rolled.</summary>
    internal static bool IsPlayerChat(XivChatType t)
    {
        var c = (int)t & 0x7F;
        return c is 10 or 11 or 12 or 13 or 14 or 15 or (>= 16 and <= 23) or 24 or 27 or 28 or 29 or 30 or 32 or 37 or (>= 101 and <= 107);
    }

    /// <summary>True if this chat line is someone else's /random. The host's own rolls ("You roll") are skipped.</summary>
    public static bool TryParse(XivChatType type, SeString sender, SeString message, out RollInfo roll)
        => TryParse(type, sender, message, false, out roll);

    /// <summary>Same as above; with <paramref name="includeSelf"/> the host's own rolls are returned too (as their character name).</summary>
    public static bool TryParse(XivChatType type, SeString sender, SeString message, bool includeSelf, out RollInfo roll)
    {
        roll = default;
        if (IsPlayerChat(type)) return false;
        var text = message.TextValue.Trim();
        if (!text.StartsWith("Random!", StringComparison.Ordinal)) return false;
        var m = RollRx.Match(text);
        if (!m.Success) return false;

        var who = m.Groups[3].Value.Trim();
        if (!int.TryParse(m.Groups[4].Value, out var value)) return false;
        var max = 999;
        if (m.Groups[5].Success && int.TryParse(m.Groups[5].Value, out var outOf)) max = outOf;
        else if (m.Groups[2].Success && int.TryParse(m.Groups[2].Value, out var mx)) max = mx;
        var custom = max != 999;

        if (who.Equals("You", StringComparison.OrdinalIgnoreCase))
        {
            if (!includeSelf) return false;
            var me = Plugin.ObjectTable.LocalPlayer;
            if (me == null) return false;
            var myName = me.Name.TextValue;
            var myWorld = string.Empty;
            try { myWorld = me.HomeWorld.Value.Name.ExtractText(); } catch { /* unknown world */ }
            roll = new RollInfo($"{myName}@{myWorld}", myName, myWorld, value, custom, max, true);
            return true;
        }

        var name = who; var world = string.Empty;
        var pp = message.Payloads.OfType<PlayerPayload>().FirstOrDefault() ?? sender.Payloads.OfType<PlayerPayload>().FirstOrDefault();
        if (pp != null)
        {
            name = pp.PlayerName;
            try { world = pp.World.Value.Name.ExtractText(); } catch { /* unknown world */ }
        }
        roll = new RollInfo($"{name}@{world}", name, world, value, custom, max);
        return true;
    }
}
