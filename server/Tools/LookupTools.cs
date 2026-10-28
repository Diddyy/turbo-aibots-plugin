using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiBots.Bots;
using Turbo.Contracts.Hotel;
using Turbo.Contracts.Plugins.Mcp;
using Turbo.Primitives.Players.Permissions;

namespace AiBots.Tools;

public sealed record FindCatalogItemInput(
    [Description("The furni's class name as the hotel names it (throne, rare_dragonlamp, chair_norja).")]
    [StringLength(64, MinimumLength = 2)]
        string Name
);

public sealed record CatalogItem(string Furni, IReadOnlyList<CatalogPrice> Offers);

public sealed record CatalogPrice(int Offer, int Credits, int Currency, string? CurrencyName);

public sealed record RoomFurniInput([Description("The AI bot's id; leave it out when you are the bot.")] int? Bot = null);

public sealed record RoomFurniList(int Room, int Total, IReadOnlyList<RoomFurniItem> Furni);

public sealed record RoomFurniItem(int Item, string Name, int X, int Y);

public sealed record HotelTime(string UtcTime, string Day, string WhatsOn);

/// <summary>Looks a furni up in the catalog players see: what it costs.</summary>
[RequiresPermission(AiBotsPermissions.CATALOG)]
public sealed class FindCatalogItemTool(IHotel hotel) : McpTool<FindCatalogItemInput, CatalogItem>
{
    public override string Name => "aibots_find_catalog_item";

    public override string Description =>
        "Looks a furni up in the catalog every player sees, by its class name: its offers and their prices in credits and in duckets or diamonds. Use it before telling anyone a price.";

    public override bool ReadOnly => true;

    public override Task<McpToolResult> HandleAsync(FindCatalogItemInput input, McpToolContext ctx, CancellationToken ct)
    {
        if (hotel.Inventory.FindFurniture(input.Name.Trim()) is not { } furni)
            return Task.FromResult(
                McpToolResult.Error(
                    $"The hotel has no furni called \"{input.Name}\". Try its class name (aibots_room_furni names the room's)."
                )
            );

        var offers = hotel.Catalog.OffersOf(furni.DefinitionId);

        if (offers.Count == 0)
            return Task.FromResult(McpToolResult.Error($"{furni.Name} is not for sale in the catalog now."));

        return Task.FromResult(
            Ok(
                new CatalogItem(
                    furni.Name,
                    [
                        .. offers
                            .Take(5)
                            .Select(x => new CatalogPrice(
                                x.Id,
                                x.CostCredits,
                                x.CostCurrency,
                                CurrencyName(x.ActivityPointType, x.CostCurrency)
                            )),
                    ]
                )
            )
        );
    }

    private static string? CurrencyName(int? type, int cost) =>
        cost <= 0 ? null
        : type switch
        {
            0 => "duckets",
            5 => "diamonds",
            _ => $"currency {type}",
        };
}

/// <summary>The furni of the AI bot's room: item ids to walk to, names to talk about.</summary>
[RequiresPermission(AiBotsPermissions.ROOM_FURNI)]
public sealed class RoomFurniTool(IHotel hotel, AiBotTarget target) : McpTool<RoomFurniInput, RoomFurniList>
{
    private const int MOST = 40;

    public override string Name => "aibots_room_furni";

    public override string Description =>
        "The floor furni of the AI bot's room (at most 40, nearest the bot first): item id, class name and tile. Use the item id with aibots_walk_to_furni.";

    public override bool ReadOnly => true;

    public override async Task<McpToolResult> HandleAsync(RoomFurniInput input, McpToolContext ctx, CancellationToken ct)
    {
        var found = await target.ResolveAsync(input.Bot, ct).ConfigureAwait(false);

        if (found.Bot is not { } bot)
            return McpToolResult.Error(found.Error!);

        var furni = await hotel.Rooms.FurniAsync(bot.Room, ct).ConfigureAwait(false);
        var at = (await hotel.Bots.InRoomAsync(bot.Room, ct).ConfigureAwait(false)).FirstOrDefault(x => x.Id == bot.BotId);

        return Ok(
            new RoomFurniList(
                bot.Room.Value,
                furni.Count,
                [
                    .. furni
                        .OrderBy(x => at is null ? 0 : Math.Max(Math.Abs(x.X - at.X), Math.Abs(x.Y - at.Y)))
                        .ThenBy(x => x.ItemId)
                        .Take(MOST)
                        .Select(x => new RoomFurniItem(
                            x.ItemId,
                            hotel.Inventory.Furniture(x.DefinitionId)?.Name ?? "furni",
                            x.X,
                            x.Y
                        )),
                ]
            )
        );
    }
}

/// <summary>The hotel's time, and what's on as staff wrote it on the plugin's panel page.</summary>
[RequiresPermission(AiBotsPermissions.TIME)]
public sealed class HotelTimeTool(IHotel hotel, IAiBotDirectory directory) : McpTool<McpNoInput, HotelTime>
{
    public override string Name => "aibots_hotel_time";

    public override string Description =>
        "The hotel's time now (UTC), the day, and what's on in the hotel (events, competitions) as staff wrote it. Use it before telling anyone the time or an event.";

    public override bool ReadOnly => true;

    public override async Task<McpToolResult> HandleAsync(McpNoInput input, McpToolContext ctx, CancellationToken ct)
    {
        var now = hotel.Clock.GetUtcNow();
        var whatsOn = (await directory.SwitchAsync(ct).ConfigureAwait(false)).WhatsOn;

        return Ok(
            new HotelTime(
                now.ToString("HH:mm", CultureInfo.InvariantCulture),
                now.ToString("dddd d MMMM yyyy", CultureInfo.InvariantCulture),
                whatsOn.Length > 0 ? whatsOn : "Nothing special is on right now."
            )
        );
    }
}
