using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiBots.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Turbo.Contracts.Ai;
using Turbo.Contracts.Hotel;
using Turbo.Primitives.Players;
using Turbo.Primitives.Rooms;

namespace AiBots.Bots;

/// <summary>An AI bot's persona as its room's owner (or staff) set it.</summary>
public sealed record AiBotPersona(
    int BotId,
    RoomId Room,
    string BotName,
    string Personality,
    string Knows,
    string Language,
    bool Enabled
);

/// <summary>The panel's kill switch and the "what's on" the bots tell.</summary>
public sealed record AiBotsSwitch(bool Stopped, string WhatsOn);

/// <summary>
/// The AI bots, the rooms whose owner turned them off and the panel's switch, over the plugin's
/// tables. What a heard line needs (is there an AI bot in this room?) is kept in memory for
/// <see cref="CacheFor"/>, so a room without one costs no read; a change made here is seen at once.
/// </summary>
public sealed class AiBotDirectory(IServiceScopeFactory scopes, IHotel hotel) : IAiBotDirectory
{
    /// <summary>How long a read is trusted: another server's change is seen after it.</summary>
    public static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _load = new(1, 1);
    private Snapshot? _snapshot;

    /// <summary>The memory key of a player's conversation with one bot: <c>bot:&lt;bot&gt;:player:&lt;player&gt;</c>.</summary>
    public static AiMemory MemoryOf(int botId, PlayerId player, bool privateConversation = false) =>
        AiMemory.ForKey($"bot:{botId}:player:{player.Value}{(privateConversation ? ":private" : string.Empty)}");

    /// <summary>The bot and player of a conversation's owner (<see cref="AiResumed.Participant"/>).</summary>
    public static (int BotId, PlayerId Player)? ParseParticipant(string participant)
    {
        var parts = participant.Split(':');

        return
            parts.Length is 5 or 6 && parts[0] == "key" && parts[1] == "bot" && parts[3] == "player"
            && (parts.Length == 5 || parts[5] == "private")
            && int.TryParse(parts[2], out var botId)
            && int.TryParse(parts[4], out var playerId)
            && botId > 0 && playerId > 0
            ? (botId, new PlayerId(playerId))
            : null;
    }

    /// <summary>The enabled AI bots of the room, when its owner has not turned them off and the panel has not stopped them.</summary>
    public async Task<IReadOnlyList<AiBotPersona>> ActiveInRoomAsync(RoomId room, CancellationToken ct)
    {
        var now = await CurrentAsync(ct).ConfigureAwait(false);

        if (now.Switch.Stopped || now.RoomsOff.Contains(room.Value))
            return [];

        return now.ByRoom.TryGetValue(room.Value, out var bots) ? bots : [];
    }

    public async Task<AiBotsSwitch> SwitchAsync(CancellationToken ct) =>
        (await CurrentAsync(ct).ConfigureAwait(false)).Switch;

    public async Task<bool> IsRoomOffAsync(RoomId room, CancellationToken ct) =>
        (await CurrentAsync(ct).ConfigureAwait(false)).RoomsOff.Contains(room.Value);

    public async Task<AiBotPersona?> FindAsync(int botId, CancellationToken ct) =>
        (await CurrentAsync(ct).ConfigureAwait(false)).ById.GetValueOrDefault(botId);

    public async Task<IReadOnlyList<AiBotRow>> AllAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AiBotsDb>();

        return await db.Bots.AsNoTracking().OrderBy(x => x.BotId).ToListAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Makes the bot an AI bot (or changes its persona): only the fields given change.</summary>
    public async Task<AiBotPersona> SaveAsync(
        HotelBot bot,
        PlayerId by,
        string? personality,
        string? knows,
        string? language,
        bool? enabled,
        CancellationToken ct
    )
    {
        var now = hotel.Clock.GetUtcNow().UtcDateTime;

        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AiBotsDb>();
            var row = await db.Bots.FirstOrDefaultAsync(x => x.BotId == bot.Id, ct).ConfigureAwait(false);

            if (row is null)
            {
                row = new AiBotRow
                {
                    BotId = bot.Id,
                    CreatedBy = by.Value,
                    CreatedAt = now,
                    Personality = "Friendly and helpful.",
                    Language = "English",
                };
                db.Bots.Add(row);
            }

            row.RoomId = bot.Room.Value;
            row.BotName = bot.Name;
            row.Personality = personality ?? row.Personality;
            row.Knows = knows ?? row.Knows;
            row.Language = language ?? row.Language;
            row.Enabled = enabled ?? row.Enabled;
            row.UpdatedAt = now;

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        Forget();

        return (await FindAsync(bot.Id, ct).ConfigureAwait(false))!;
    }

    /// <summary>Turns an AI bot on or off from the panel; false when there is no such AI bot.</summary>
    public async Task<bool> SetEnabledAsync(int botId, bool enabled, CancellationToken ct)
    {
        int changed;

        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AiBotsDb>();
            changed = await db
                .Bots.Where(x => x.BotId == botId)
                .ExecuteUpdateAsync(x => x.SetProperty(r => r.Enabled, enabled), ct)
                .ConfigureAwait(false);
        }

        Forget();

        return changed > 0;
    }

    /// <summary>Makes the AI bot a plain bot again: its persona and figures go.</summary>
    public async Task<bool> RemoveAsync(int botId, CancellationToken ct)
    {
        int removed;

        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AiBotsDb>();
            removed = await db.Bots.Where(x => x.BotId == botId).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        }

        Forget();

        return removed > 0;
    }

    /// <summary>A room's owner turns its AI bots off (or on again).</summary>
    public async Task SetRoomOffAsync(RoomId room, bool off, PlayerId by, CancellationToken ct)
    {
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AiBotsDb>();
            var row = await db.RoomsOff.FirstOrDefaultAsync(x => x.RoomId == room.Value, ct).ConfigureAwait(false);

            if (off && row is null)
                db.RoomsOff.Add(new AiRoomOffRow { RoomId = room.Value, By = by.Value, At = hotel.Clock.GetUtcNow().UtcDateTime });
            else if (!off && row is not null)
                db.RoomsOff.Remove(row);

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        Forget();
    }

    /// <summary>The panel stops every AI bot (or lets them answer again).</summary>
    public async Task SetStoppedAsync(bool stopped, PlayerId by, CancellationToken ct) =>
        await ChangeSwitchAsync(
                row =>
                {
                    row.Stopped = stopped;
                    row.StoppedBy = stopped ? by.Value : 0;
                    row.StoppedAt = stopped ? hotel.Clock.GetUtcNow().UtcDateTime : null;
                },
                ct
            )
            .ConfigureAwait(false);

    public async Task SetWhatsOnAsync(string text, CancellationToken ct) =>
        await ChangeSwitchAsync(row => row.WhatsOn = text, ct).ConfigureAwait(false);

    /// <summary>Adds a reply's (or a fallback's) usage to the bot's figures.</summary>
    public async Task CountAsync(int botId, bool replied, AiUsage usage, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AiBotsDb>();
        var now = hotel.Clock.GetUtcNow().UtcDateTime;

        await db
            .Bots.Where(x => x.BotId == botId)
            .ExecuteUpdateAsync(
                x =>
                    x.SetProperty(r => r.Replies, r => r.Replies + (replied ? 1 : 0))
                        .SetProperty(r => r.Fallbacks, r => r.Fallbacks + (replied ? 0 : 1))
                        .SetProperty(r => r.TokensIn, r => r.TokensIn + usage.InputTokens)
                        .SetProperty(r => r.TokensOut, r => r.TokensOut + usage.OutputTokens)
                        .SetProperty(r => r.LastReplyAt, now),
                ct
            )
            .ConfigureAwait(false);
    }

    /// <summary>Drops what is kept in memory: the next ask reads the tables.</summary>
    public void Forget() => _snapshot = null;

    private async Task ChangeSwitchAsync(Action<AiSwitchRow> change, CancellationToken ct)
    {
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AiBotsDb>();
            var row = await db.Switch.FirstOrDefaultAsync(x => x.Id == 1, ct).ConfigureAwait(false);

            if (row is null)
                db.Switch.Add(row = new AiSwitchRow { Id = 1 });

            change(row);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        Forget();
    }

    private async Task<Snapshot> CurrentAsync(CancellationToken ct)
    {
        var now = hotel.Clock.GetUtcNow();

        if (_snapshot is { } kept && now - kept.ReadAt < CacheFor)
            return kept;

        await _load.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            if (_snapshot is { } again && now - again.ReadAt < CacheFor)
                return again;

            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AiBotsDb>();
            var bots = await db.Bots.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
            var off = await db.RoomsOff.AsNoTracking().Select(x => x.RoomId).ToListAsync(ct).ConfigureAwait(false);
            var row = await db.Switch.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false);
            var personas = bots.Select(ToPersona).ToList();

            return _snapshot = new Snapshot(
                now,
                personas.ToDictionary(x => x.BotId),
                personas
                    .Where(x => x.Enabled)
                    .GroupBy(x => x.Room.Value)
                    .ToDictionary(x => x.Key, x => (IReadOnlyList<AiBotPersona>)[.. x]),
                [.. off],
                new AiBotsSwitch(row?.Stopped ?? false, row?.WhatsOn ?? string.Empty)
            );
        }
        finally
        {
            _load.Release();
        }
    }

    private static AiBotPersona ToPersona(AiBotRow row) =>
        new(row.BotId, new RoomId(row.RoomId), row.BotName, row.Personality, row.Knows, row.Language, row.Enabled);

    private sealed record Snapshot(
        DateTimeOffset ReadAt,
        IReadOnlyDictionary<int, AiBotPersona> ById,
        IReadOnlyDictionary<int, IReadOnlyList<AiBotPersona>> ByRoom,
        HashSet<int> RoomsOff,
        AiBotsSwitch Switch
    );
}
