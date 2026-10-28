using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Turbo.Contracts.Ai;
using Turbo.Primitives.Moderation;

namespace AiBots.Bots;

/// <summary>
/// What an AI bot's answer goes through before anyone sees it, whatever the model wrote: an answer
/// that repeats its instructions, or holds an email, a phone number or a link, is replaced by the
/// refusal line; the rest goes through the hotel's word filter and is cut to what a bot says.
/// The room's own filter runs again as the bot speaks.
/// </summary>
public static partial class AiBotGuard
{
    /// <summary>The most an AI bot says in one answer: two chat bubbles' worth.</summary>
    public const int MAX_LENGTH = 200;

    /// <summary>The answer as a player may see it, or null when it must be refused.</summary>
    public static string? Check(string text, IWordFilter filter)
    {
        var trimmed = text.Trim();

        if (trimmed.Length == 0)
            return string.Empty;

        if (Leaks(trimmed) || Email().IsMatch(trimmed) || Phone().IsMatch(trimmed) || Link().IsMatch(trimmed))
            return null;

        var filtered = filter.Filter(trimmed);

        return filtered.Length <= MAX_LENGTH ? filtered : string.Concat(filtered.AsSpan(0, MAX_LENGTH - 3), "...");
    }

    /// <summary>The answer repeats the bot's instructions.</summary>
    public static bool Leaks(string text) =>
        text.Contains(AiBotPrompt.MARKER, StringComparison.OrdinalIgnoreCase)
        || AiBotPrompt.Telltales.Any(x => text.Contains(x, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase)]
    private static partial Regex Email();

    // Seven digits or more, spaces, dots, dashes or brackets between them.
    [GeneratedRegex(@"\+?\d(?:[\s().-]*\d){6,}")]
    private static partial Regex Phone();

    [GeneratedRegex(@"https?://|www\.|\b[a-z0-9-]+\.(?:com|net|org|gg|io|ly|me|tv|co|xyz)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Link();
}

/// <summary>
/// The guard as middleware: each answer's text is checked as it comes back, so the conversation
/// kept (what is sent is what is remembered) never holds what was refused.
/// </summary>
public sealed class AiBotModeration(IWordFilter filter, AiBotTexts texts) : IAiMiddleware
{
    public async Task<AiAnswer> HandleAsync(AiStep step, AiNext next, CancellationToken ct)
    {
        var answer = await next(step, ct).ConfigureAwait(false);
        var parts = answer.Message.Parts;

        if (!parts.OfType<AiTextPart>().Any())
            return answer;

        var refusal = await texts.GetAsync(AiBotTexts.REFUSED, ct).ConfigureAwait(false);

        var guarded = parts
            .Where(part => part is not AiTextPart)
            .Prepend(new AiTextPart(AiBotGuard.Check(answer.Message.Text, filter) ?? refusal))
            .ToImmutableArray();

        return answer with { Message = answer.Message with { Parts = guarded } };
    }
}
