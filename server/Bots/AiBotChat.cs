using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Turbo.Contracts.Ai;
using Turbo.Contracts.Hotel;
using Turbo.Primitives.Players;
using Turbo.Primitives.Rooms;
using Turbo.Primitives.Rooms.Enums;
using Turbo.Primitives.Rooms.Events;
using Turbo.Primitives.Rooms.Events.Player;

namespace AiBots.Bots;

public sealed record AiBotHeard(RoomId Room, PlayerId Player, string Text, RoomChatType Type, PlayerId? TargetPlayer, string? Recipient, int? X, int? Y);

/// <summary>Copies only plain event data in the room's turn. Private player whispers are never queued.</summary>
public sealed class AiBotListener(AiBotWork work, AiBotChat chat, IOptionsMonitor<AiBotsSettings> settings) : IRoomEventListener
{
    public Task OnRoomEventAsync(RoomEvent evt, CancellationToken ct)
    {
        if (settings.CurrentValue.Enabled && evt is PlayerChatEvent { IsCancelled: false } heard
            && (heard.ChatType != RoomChatType.Whisper || heard.TargetPlayerId is null && !string.IsNullOrWhiteSpace(heard.RecipientName)))
        {
            var copy = new AiBotHeard(heard.RoomId, heard.PlayerId, heard.Text, heard.ChatType, heard.TargetPlayerId, heard.RecipientName, heard.SpeakerX, heard.SpeakerY);
            work.TryEnqueue(token => chat.HandleAsync(copy, token));
        }
        return Task.CompletedTask;
    }
}

public sealed class AiBotChat(IHotel hotel, IAi ai, IAiBotDirectory directory, AiBotScenes scenes, AiBotAgent agent, AiBotReplies replies, IOptionsMonitor<AiBotsSettings> settings)
{
    public async Task HandleAsync(AiBotHeard heard, CancellationToken ct)
    {
        if (!settings.CurrentValue.Enabled || heard.Text.Length == 0
            || heard.Type == RoomChatType.Whisper && (heard.TargetPlayer is not null || string.IsNullOrWhiteSpace(heard.Recipient)))
            return;
        if (await hotel.Players.CurrentRoomAsync(heard.Player, ct).ConfigureAwait(false) != heard.Room)
            return;
        var active = await directory.ActiveInRoomAsync(heard.Room, ct).ConfigureAwait(false);
        if (active.Count == 0) return;
        var present = await hotel.Bots.InRoomAsync(heard.Room, ct).ConfigureAwait(false);
        var eligible = from persona in active
                       join bot in present on persona.BotId equals bot.Id
                       where bot.Room == heard.Room
                       select (Persona: persona, Bot: bot);
        var target = heard.Type == RoomChatType.Whisper
            ? eligible.FirstOrDefault(x => string.Equals(x.Bot.Name, heard.Recipient, StringComparison.OrdinalIgnoreCase))
            : eligible.OrderByDescending(x => Mentioned(heard.Text, x.Bot.Name))
                .ThenBy(x => Distance(heard, x.Bot)).ThenBy(x => x.Bot.Id)
                .FirstOrDefault(x => Mentioned(heard.Text, x.Bot.Name) || Distance(heard, x.Bot) <= settings.CurrentValue.NearTiles);
        if (target.Persona is null) return;
        var player = await hotel.Players.GetAsync(heard.Player, ct).ConfigureAwait(false);
        if (player is null) return;
        using var scene = scenes.Enter(new AiBotScene(target.Persona, heard.Room, heard.Player, player.Name, heard.Type == RoomChatType.Whisper));
        var nodes = settings.CurrentValue.GivableBadges.Count > 0
            ? AiBotsPermissions.BotTools.Append(AiBotsPermissions.GIVE_BADGE).ToArray()
            : AiBotsPermissions.BotTools;
        var answer = await ai.PromptAsync(agent, AiPrompt.FromPlayer(heard.Text), new AiCall {
            As = AiActor.System(nodes), For = heard.Player,
            Memory = AiBotDirectory.MemoryOf(target.Bot.Id, heard.Player, heard.Type == RoomChatType.Whisper),
        }, ct).ConfigureAwait(false);
        await replies.DeliverAsync(target.Persona, heard.Player, answer, heard.Type == RoomChatType.Whisper, ct).ConfigureAwait(false);
    }

    private static int Distance(AiBotHeard heard, HotelBot bot) => heard.X is { } x && heard.Y is { } y ? Math.Max(Math.Abs(x - bot.X), Math.Abs(y - bot.Y)) : int.MaxValue;

    public static bool Mentioned(string text, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var from = 0;
        while (from < text.Length)
        {
            var at = text.IndexOf(name, from, StringComparison.OrdinalIgnoreCase);
            if (at < 0) return false;
            var end = at + name.Length;
            if ((at == 0 || !char.IsLetterOrDigit(text[at - 1])) && (end == text.Length || !char.IsLetterOrDigit(text[end]))) return true;
            from = end;
        }
        return false;
    }
}
