using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Turbo.Contracts.Hotel;
using Turbo.Contracts.Plugins.Mcp;
using Turbo.Primitives.Players.Permissions;

namespace AiBots.Tools;

public sealed record WalkToTileInput(
    [Description("The tile's x.")] [Range(0, 255)] int X,
    [Description("The tile's y.")] [Range(0, 255)] int Y,
    [Description("The AI bot's id; leave it out when you are the bot.")] int? Bot = null
);

public sealed record WalkToFurniInput(
    [Description("The furni's item id, from aibots_room_furni.")] [Range(1, int.MaxValue)] int Item,
    [Description("The AI bot's id; leave it out when you are the bot.")] int? Bot = null
);

public sealed record BotMoved(int Bot, string Result);

/// <summary>The AI bot walks to a tile of its room.</summary>
[RequiresPermission(AiBotsPermissions.WALK)]
public sealed class WalkToTileTool(IHotel hotel, AiBotTarget target) : McpTool<WalkToTileInput, BotMoved>
{
    public override string Name => "aibots_walk_to_tile";

    public override string Description =>
        "Walks the AI bot to a tile of its room (x, y). Answers whether it is on its way; it walks there in a few seconds.";

    // It only moves the bot: nothing to approve.
    public override bool Destructive => false;

    public override async Task<McpToolResult> HandleAsync(WalkToTileInput input, McpToolContext ctx, CancellationToken ct)
    {
        var found = await target.ResolveAsync(input.Bot, ct).ConfigureAwait(false);

        if (found.Bot is not { } bot)
            return McpToolResult.Error(found.Error!);

        return await hotel.Bots.WalkToAsync(bot.Room, bot.BotId, input.X, input.Y, ct).ConfigureAwait(false)
            ? Ok(new BotMoved(bot.BotId, "on its way"))
            : McpToolResult.Error(
                $"The bot can't walk to ({input.X}, {input.Y}): no way leads there, it is already there, or no one is in its room."
            );
    }
}

/// <summary>The AI bot walks onto a furni of its room.</summary>
[RequiresPermission(AiBotsPermissions.WALK)]
public sealed class WalkToFurniTool(IHotel hotel, AiBotTarget target) : McpTool<WalkToFurniInput, BotMoved>
{
    public override string Name => "aibots_walk_to_furni";

    public override string Description =>
        "Walks the AI bot onto a furni of its room, by the item id aibots_room_furni answers. Answers whether it is on its way.";

    public override bool Destructive => false;

    public override async Task<McpToolResult> HandleAsync(WalkToFurniInput input, McpToolContext ctx, CancellationToken ct)
    {
        var found = await target.ResolveAsync(input.Bot, ct).ConfigureAwait(false);

        if (found.Bot is not { } bot)
            return McpToolResult.Error(found.Error!);

        return await hotel.Bots.WalkToFurniAsync(bot.Room, bot.BotId, input.Item, ct).ConfigureAwait(false)
            ? Ok(new BotMoved(bot.BotId, "on its way"))
            : McpToolResult.Error(
                $"The bot can't walk to item {input.Item}: there is no such furni in its room, or no way to it. Look the furni up with aibots_room_furni."
            );
    }
}
