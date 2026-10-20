using System;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;

namespace TLP.Games;

/// <summary>One venue game. Add a new game by implementing this and registering it in Plugin.cs.</summary>
public interface IGame : IDisposable
{
    string Name { get; }

    /// <summary>Draws the game's own UI inside the TLP window.</summary>
    void Draw();

    /// <summary>Called every frame, even when the window is closed (timers).</summary>
    void Update();

    /// <summary>Every chat line is passed to every game; each game ignores what it doesn't need.</summary>
    void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled);
}
