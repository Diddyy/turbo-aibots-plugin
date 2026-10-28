using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AiBots.Database;
using Turbo.Contracts.Ai;
using Turbo.Contracts.Hotel;
using Turbo.Primitives.Players;
using Turbo.Primitives.Rooms;

namespace AiBots.Bots;

public interface IAiBotDirectory
{
    Task<IReadOnlyList<AiBotPersona>> ActiveInRoomAsync(RoomId room, CancellationToken ct);
    Task<AiBotsSwitch> SwitchAsync(CancellationToken ct);
    Task<bool> IsRoomOffAsync(RoomId room, CancellationToken ct);
    Task<AiBotPersona?> FindAsync(int botId, CancellationToken ct);
    Task<IReadOnlyList<AiBotRow>> AllAsync(CancellationToken ct);
    Task<AiBotPersona> SaveAsync(HotelBot bot, PlayerId by, string? personality, string? knows, string? language, bool? enabled, CancellationToken ct);
    Task<bool> SetEnabledAsync(int botId, bool enabled, CancellationToken ct);
    Task<bool> RemoveAsync(int botId, CancellationToken ct);
    Task SetRoomOffAsync(RoomId room, bool off, PlayerId by, CancellationToken ct);
    Task SetStoppedAsync(bool stopped, PlayerId by, CancellationToken ct);
    Task SetWhatsOnAsync(string text, CancellationToken ct);
    Task CountAsync(int botId, bool replied, AiUsage usage, CancellationToken ct);
}
