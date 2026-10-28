using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Turbo.Contracts.Ai;
using Turbo.Contracts.Hotel;
using Turbo.Primitives.Players;
using Turbo.Primitives.Rooms;

namespace AiBots.Bots;

/// <summary>Owner/staff checks happen off-turn and again when queued work executes.</summary>
public sealed class AiBotManagement(IHotel hotel, IAiBotDirectory directory, IAi ai, AiBotAgent agent, IOptionsMonitor<AiBotsSettings> settings)
{
    public async Task<string> ConfigureAsync(PlayerId player, RoomId room, string arguments, CancellationToken ct)
    {
        if (await hotel.Players.CurrentRoomAsync(player, ct).ConfigureAwait(false) != room)
            return "Enter the room again before changing its bots.";
        var staff = await hotel.Players.HasPermissionAsync(player, AiBotsPermissions.MANAGE_ANY, ct).ConfigureAwait(false);
        var info = await hotel.Rooms.GetAsync(room, ct).ConfigureAwait(false);
        if (!staff && (!settings.CurrentValue.OwnersMayCreate || info?.Owner != player))
            return "Only this room's owner or staff may change its AI bots.";
        var args = arguments.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var bots = await hotel.Bots.InRoomAsync(room, ct).ConfigureAwait(false);
        if (args.Length == 0)
            return "Bots: " + string.Join(", ", bots.Select(x => $"{x.Id} {x.Name}")) + ". Use :aibot <id> personality <text>, knows <text>, language <text>, on, off or remove; :aibot room on|off.";
        if (args.Length < 2) return "Name a bot id and what to change: personality, knows, language, on, off or remove.";
        var action = args[1].ToLowerInvariant();
        if (args[0].Equals("room", StringComparison.OrdinalIgnoreCase))
        {
            if (action is not ("on" or "off")) return "Use :aibot room on or :aibot room off.";
            await directory.SetRoomOffAsync(room, action == "off", player, ct).ConfigureAwait(false);
            return action == "off" ? "AI bots in this room are off." : "AI bots in this room are on.";
        }
        var bot = bots.FirstOrDefault(x => args[0] == x.Id.ToString() || string.Equals(x.Name, args[0], StringComparison.OrdinalIgnoreCase));
        if (bot is null) return "There is no bot by that id or name in this room. Use :aibot to list them.";
        if (action == "remove")
        {
            await directory.RemoveAsync(bot.Id, ct).ConfigureAwait(false);
            return $"{bot.Name} is a plain bot again.";
        }
        if (action is not ("personality" or "knows" or "language" or "on" or "off"))
            return "Choose personality, knows, language, on, off or remove.";
        var text = args.Length == 3 ? args[2] : string.Empty;
        var max = action == "knows" ? 300 : action == "language" ? 24 : 200;
        if (action is "personality" or "knows" or "language" && (text.Length == 0 || text.Length > max))
            return $"Use 1 to {max} characters for {action}.";
        await directory.SaveAsync(bot, player, action == "personality" ? text : null,
            action == "knows" ? text : null, action == "language" ? text : null,
            action is "on" or "off" ? action == "on" : null, ct).ConfigureAwait(false);
        return $"{bot.Name}'s AI settings were saved.";
    }

    public async Task ForgetAsync(PlayerId player, CancellationToken ct)
    {
        foreach (var bot in await directory.AllAsync(ct).ConfigureAwait(false))
        {
            await ai.ForgetAsync(agent, AiBotDirectory.MemoryOf(bot.BotId, player), ct).ConfigureAwait(false);
            await ai.ForgetAsync(agent, AiBotDirectory.MemoryOf(bot.BotId, player, true), ct).ConfigureAwait(false);
        }
    }
}
