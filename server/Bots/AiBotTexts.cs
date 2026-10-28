using System.Threading;
using System.Threading.Tasks;
using Turbo.Primitives.Texts;

namespace AiBots.Bots;

/// <summary>
/// The lines an AI bot says when it can't answer, from the hotel's texts (gamedata/external_texts.txt,
/// which staff can change), with the plugin's own words until they are there.
/// </summary>
public sealed class AiBotTexts(IHotelTextProvider? texts = null)
{
    public const string DAILY = "aibots.fallback.daily";
    public const string BUSY = "aibots.fallback.busy";
    public const string WAITING = "aibots.fallback.waiting";
    public const string UNAVAILABLE = "aibots.fallback.unavailable";
    public const string LOST = "aibots.fallback.lost";
    public const string REFUSED = "aibots.fallback.refused";

    public async Task<string> GetAsync(string key, CancellationToken ct)
    {
        var text = texts is null ? null : await texts.GetTextAsync(key, ct).ConfigureAwait(false);

        return string.IsNullOrWhiteSpace(text) ? Default(key) : text;
    }

    public static string Default(string key) =>
        key switch
        {
            DAILY => "I've talked so much today! Let's chat again tomorrow.",
            BUSY => "One at a time, please! Ask me again in a moment.",
            WAITING => "I've asked the staff about that. I'll tell you when they answer.",
            UNAVAILABLE => "I'm resting right now. Try me again later.",
            REFUSED => "I can't talk about that. Let's talk about something else!",
            _ => "Sorry, I lost my train of thought. Can you ask me again?",
        };
}
