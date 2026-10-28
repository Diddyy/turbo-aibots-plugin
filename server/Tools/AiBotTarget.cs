using System.Threading;
using System.Threading.Tasks;
using AiBots.Bots;

namespace AiBots.Tools;

/// <summary>The AI bot a tool acts on, or why it may not.</summary>
public sealed record AiBotTargetResult(AiBotPersona? Bot, AiBotScene? Scene, string? Error);

/// <summary>
/// Which AI bot a tool acts on. In a bot's own answer it is that bot, in its room, and naming
/// another is refused: a bot never moves or speaks for another. Called by staff's assistant over MCP
/// (no scene), it is the AI bot the call names.
/// </summary>
public sealed class AiBotTarget(AiBotScenes scenes, IAiBotDirectory directory)
{
    public AiBotScene? Scene => scenes.Current;

    public async Task<AiBotTargetResult> ResolveAsync(int? botId, CancellationToken ct)
    {
        if (scenes.Current is { } scene)
            return botId is { } named && named != scene.Bot.BotId
                ? new(null, scene, $"You can only act as yourself, bot {scene.Bot.BotId}. Leave \"bot\" out.")
                : new(scene.Bot, scene, null);

        if (botId is not { } id)
            return new(null, null, "Name the AI bot to act as with \"bot\" (its bot id).");

        return await directory.FindAsync(id, ct).ConfigureAwait(false) is { } bot
            ? new(bot, null, null)
            : new(null, null, $"Bot {id} is not an AI bot. Make it one with :aibot in its room first.");
    }
}
