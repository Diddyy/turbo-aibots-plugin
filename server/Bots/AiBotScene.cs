using System;
using System.Threading;
using Turbo.Primitives.Players;
using Turbo.Primitives.Rooms;

namespace AiBots.Bots;

/// <summary>Who an AI bot is answering, where: the bot, its persona, its room and the player who spoke.</summary>
public sealed record AiBotScene(AiBotPersona Bot, RoomId Room, PlayerId Player, string PlayerName, bool Private = false);

/// <summary>
/// The scene of the answer running now, for the bot's tools: a tool the bot calls acts on that bot
/// in that room, and nothing else. A tool called by staff's assistant over MCP runs with no scene
/// and names its bot.
/// </summary>
public sealed class AiBotScenes
{
    private readonly AsyncLocal<AiBotScene?> _current = new();

    public AiBotScene? Current => _current.Value;

    /// <summary>The scene for the run started inside it; disposing it ends it.</summary>
    public IDisposable Enter(AiBotScene scene)
    {
        var before = _current.Value;
        _current.Value = scene;

        return new Leave(this, before);
    }

    private sealed class Leave(AiBotScenes scenes, AiBotScene? before) : IDisposable
    {
        public void Dispose() => scenes._current.Value = before;
    }
}
