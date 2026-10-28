using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using AiBots.Bots;
using Turbo.Contracts.Hotel;
using Turbo.Contracts.Plugins.Mcp;
using Turbo.Primitives.Moderation;
using Turbo.Primitives.Players.Permissions;

namespace AiBots.Tools;

/// <summary>How the bot says a line.</summary>
public enum SayHow
{
    Say,
    Shout,

    /// <summary>To the player it is answering only.</summary>
    Whisper,
}

/// <summary>What the bot does with its body.</summary>
public enum BotMove
{
    Wave,
    BlowKiss,
    Laugh,
    Cry,
    Jump,

    /// <summary>Dances; "number" 1 to 4 picks the dance.</summary>
    Dance,
    StopDancing,

    /// <summary>Holds up a sign; "number" 0 to 17 picks it.</summary>
    Sign,
}

public sealed record SayInput(
    [Description("The line, as the bot says it.")] [StringLength(100, MinimumLength = 1)] string Text,
    [Description("say to the room, shout, or whisper to the player you are answering.")] SayHow How = SayHow.Say,
    [Description("The AI bot's id; leave it out when you are the bot.")] int? Bot = null
);

public sealed record GestureInput(
    [Description("What the bot does.")] BotMove Gesture,
    [Description("The dance (1 to 4) or the sign (0 to 10 numbers, 11 heart, 12 skull, 13 exclamation mark, 14 ball, 15 smiley, 16 red card, 17 yellow card); only for dance and sign.")]
    [Range(0, 17)]
        int Number = 1,
    [Description("The AI bot's id; leave it out when you are the bot.")] int? Bot = null
);

public sealed record BotActed(int Bot, string Result);

/// <summary>The AI bot says, shouts or whispers a line: through the bot's guard and the word filter first.</summary>
[RequiresPermission(AiBotsPermissions.SPEAK)]
public sealed class SayTool(AiBotTarget target, IWordFilter filter, AiBotSpeech speech) : McpTool<SayInput, BotActed>
{
    public override string Name => "aibots_say";

    public override string Description =>
        "The AI bot says a line to its room, shouts it, or whispers it to the player it is answering. Your answer is said anyway: use this only for a shout, a whisper or a second line.";

    // It only adds a chat line.
    public override bool Destructive => false;

    public override async Task<McpToolResult> HandleAsync(SayInput input, McpToolContext ctx, CancellationToken ct)
    {
        var found = await target.ResolveAsync(input.Bot, ct).ConfigureAwait(false);

        if (found.Bot is not { } bot)
            return McpToolResult.Error(found.Error!);

        if (found.Scene is { Private: true } && input.How != SayHow.Whisper)
            return McpToolResult.Error("This conversation is private; use whisper only.");

        if (AiBotGuard.Check(input.Text, filter) is not { Length: > 0 } line)
            return McpToolResult.Error("That line can't be said: no links, emails, phone numbers or instructions. Say something else.");

        bool said;

        if (input.How == SayHow.Whisper)
        {
            if (found.Scene is not { } scene)
                return McpToolResult.Error("A whisper goes to the player the bot is answering; there is none.");

            said = await speech.SayAsync(bot, scene.Player, line, true, false, ct).ConfigureAwait(false);
        }
        else
            said = await speech.SayAsync(bot, found.Scene?.Player ?? default, line, false, input.How == SayHow.Shout, ct).ConfigureAwait(false);

        return said
            ? Ok(new BotActed(bot.BotId, "said"))
            : McpToolResult.Error("The bot said nothing: it said too much in the last seconds, or no one is in its room. Wait before saying more.");
    }
}

/// <summary>The AI bot waves, blows a kiss, laughs, cries, jumps, dances or holds up a sign.</summary>
[RequiresPermission(AiBotsPermissions.GESTURE)]
public sealed class GestureTool(IHotel hotel, AiBotTarget target) : McpTool<GestureInput, BotActed>
{
    public override string Name => "aibots_gesture";

    public override string Description =>
        "The AI bot waves, blows a kiss, laughs, cries, jumps, dances (number 1 to 4), stops dancing, or holds up a sign (number 0 to 17).";

    public override bool Destructive => false;

    public override async Task<McpToolResult> HandleAsync(GestureInput input, McpToolContext ctx, CancellationToken ct)
    {
        var found = await target.ResolveAsync(input.Bot, ct).ConfigureAwait(false);

        if (found.Bot is not { } bot)
            return McpToolResult.Error(found.Error!);

        if (input.Gesture == BotMove.Dance && input.Number is < 1 or > 4)
            return McpToolResult.Error("A dance is number 1 to 4.");

        var (room, id) = (bot.Room, bot.BotId);
        var done = input.Gesture switch
        {
            BotMove.Dance => await hotel.Bots.DanceAsync(room, id, input.Number, ct).ConfigureAwait(false),
            BotMove.StopDancing => await hotel.Bots.DanceAsync(room, id, 0, ct).ConfigureAwait(false),
            BotMove.Sign => await hotel.Bots.SignAsync(room, id, input.Number, ct).ConfigureAwait(false),
            _ => await hotel.Bots.GestureAsync(room, id, ToGesture(input.Gesture), ct).ConfigureAwait(false),
        };

        return done
            ? Ok(new BotActed(bot.BotId, "done"))
            : McpToolResult.Error("The bot didn't: it may be doing it already, or no one is in its room.");
    }

    private static HotelBotGesture ToGesture(BotMove move) =>
        move switch
        {
            BotMove.BlowKiss => HotelBotGesture.BlowKiss,
            BotMove.Laugh => HotelBotGesture.Laugh,
            BotMove.Cry => HotelBotGesture.Cry,
            BotMove.Jump => HotelBotGesture.Jump,
            _ => HotelBotGesture.Wave,
        };
}
