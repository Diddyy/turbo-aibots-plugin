using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Turbo.Contracts.Ai;
using Turbo.Contracts.Hotel;
using Turbo.Primitives.Moderation;
using Turbo.Primitives.Players;

namespace AiBots.Bots;

/// <summary>All plugin speech, including whispers, shares a bounded ten-lines-per-five-seconds gate.</summary>
public sealed class AiBotSpeech(IHotel hotel, IWordFilter filter)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<int, Queue<DateTimeOffset>> _lines = [];

    public async Task<bool> SayAsync(AiBotPersona bot, PlayerId player, string text, bool whisper, bool shout, CancellationToken ct)
    {
        if (AiBotGuard.Check(text, filter) is not { Length: > 0 } safe) return false;
        var now = hotel.Clock.GetUtcNow();
        lock (_gate)
        {
            foreach (var key in _lines.Keys.ToArray())
            {
                var times = _lines[key];
                while (times.TryPeek(out var at) && now - at >= TimeSpan.FromSeconds(5)) times.Dequeue();
                if (times.Count == 0) _lines.Remove(key);
            }
            if (!_lines.TryGetValue(bot.BotId, out var lines))
            {
                if (_lines.Count >= 1024) return false;
                _lines[bot.BotId] = lines = new();
            }
            if (lines.Count >= 10) return false;
            lines.Enqueue(now);
        }
        return whisper
            ? await hotel.Bots.WhisperAsync(bot.Room, bot.BotId, player, safe, ct).ConfigureAwait(false)
            : await hotel.Bots.SayAsync(bot.Room, bot.BotId, safe, shout, ct).ConfigureAwait(false);
    }
}

public sealed class AiBotReplies(IHotel hotel, IAiBotDirectory directory, AiBotSpeech speech, AiBotTexts texts, IOptionsMonitor<AiBotsSettings> settings)
{
    public async Task DeliverAsync(AiBotPersona bot, PlayerId player, AiResponse answer, bool whisper, CancellationToken ct)
    {
        if (!settings.CurrentValue.Enabled || answer.Stop == AiStopReason.Cancelled
            || await hotel.Players.CurrentRoomAsync(player, ct).ConfigureAwait(false) != bot.Room
            || !(await directory.ActiveInRoomAsync(bot.Room, ct).ConfigureAwait(false)).Any(x => x.BotId == bot.BotId)) return;
        var line = answer.Stop switch {
            AiStopReason.Finished or AiStopReason.StopCondition when answer.Text.Length > 0 => answer.Text,
            AiStopReason.DailyBudget => await texts.GetAsync(AiBotTexts.DAILY, ct).ConfigureAwait(false),
            AiStopReason.RateLimited or AiStopReason.Budget => await texts.GetAsync(AiBotTexts.BUSY, ct).ConfigureAwait(false),
            AiStopReason.AwaitingApproval => await texts.GetAsync(AiBotTexts.WAITING, ct).ConfigureAwait(false),
            AiStopReason.Unavailable => await texts.GetAsync(AiBotTexts.UNAVAILABLE, ct).ConfigureAwait(false),
            _ => await texts.GetAsync(AiBotTexts.LOST, ct).ConfigureAwait(false),
        };
        var said = await speech.SayAsync(bot, player, line, whisper, false, ct).ConfigureAwait(false);
        await directory.CountAsync(bot.BotId, said && answer.Ok, answer.Usage, ct).ConfigureAwait(false);
    }

    public async Task ResumedAsync(AiResumed resumed, CancellationToken ct)
    {
        if (AiBotDirectory.ParseParticipant(resumed.Participant) is not { } owner
            || resumed.Player is { } payer && payer != owner.Player) return;
        if (await directory.FindAsync(owner.BotId, ct).ConfigureAwait(false) is { } bot)
            await DeliverAsync(bot, owner.Player, resumed.Response, whisper: true, ct).ConfigureAwait(false);
    }
}
