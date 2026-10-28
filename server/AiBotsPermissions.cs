using System.Collections.Generic;
using Turbo.Primitives.Players.Permissions;

namespace AiBots;

/// <summary>
/// The plugin's permission nodes. An AI bot's tools run as the hotel holding exactly the tool
/// nodes below (never a player's rank), and each acts on that bot in its own room only; staff's AI
/// assistants are given the same tools over MCP by these nodes.
/// </summary>
public sealed class AiBotsPermissions : IPermissionNodeSource
{
    public const string COMMAND = "aibots.command.aibot";
    public const string FORGET = "aibots.command.forget";
    public const string MANAGE_ANY = "aibots.manage.any";

    public const string WALK = "aibots.bot.walk";
    public const string SPEAK = "aibots.bot.speak";
    public const string GESTURE = "aibots.bot.gesture";
    public const string CATALOG = "aibots.catalog.lookup";
    public const string ROOM_FURNI = "aibots.room.furni";
    public const string TIME = "aibots.hotel.time";
    public const string GIVE_BADGE = "aibots.badge.give";

    /// <summary>What an AI bot holds as it answers: every tool but giving a badge, which it holds only when the hotel lists badges to give.</summary>
    public static readonly string[] BotTools = [WALK, SPEAK, GESTURE, CATALOG, ROOM_FURNI, TIME];

    public string? Prefix => "aibots";

    public IEnumerable<PermissionNodeDefinition> Nodes =>
        [
            new(COMMAND, "AI bots: turn the bots in your own room into AI bots with :aibot", GrantedByDefault: true),
            new(FORGET, "AI bots: make every AI bot forget your conversations with :forgetbots", GrantedByDefault: true),
            new(MANAGE_ANY, "AI bots: turn any bot in any room into an AI bot, or back"),
            new(WALK, "AI bots tool: walk an AI bot to a tile or a furni of its room"),
            new(SPEAK, "AI bots tool: an AI bot says, shouts or whispers a line"),
            new(GESTURE, "AI bots tool: an AI bot waves, dances or holds up a sign"),
            new(CATALOG, "AI bots tool: look a furni up in the catalog"),
            new(ROOM_FURNI, "AI bots tool: list the furni of an AI bot's room"),
            new(TIME, "AI bots tool: the hotel's time and what's on"),
            new(GIVE_BADGE, "AI bots tool: give a player one of the hotel's givable badges (staff approve each gift)"),
        ];

    public IEnumerable<PermissionMetaDefinition> MetaKeys => [];
}
