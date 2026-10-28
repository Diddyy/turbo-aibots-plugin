using System.Collections.Generic;
using Turbo.Contracts.Plugins;

namespace AiBots;

/// <summary>
/// The hotel's policy for AI bots. Which provider answers, its key, each player's daily tokens and
/// runs a minute are the hotel's AI settings (<c>Turbo:Ai</c>), and the plugin uses models only
/// once its key is in <c>Turbo:Ai:Plugins</c>.
/// </summary>
[PluginSettings("AiBots")]
public sealed class AiBotsSettings
{
    /// <summary>AI bots answer players. Off: every AI bot of the hotel is a plain bot again (their personas are kept).</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Room owners may turn the bots in their own rooms into AI bots with :aibot. Off: only staff holding aibots.manage.any may.</summary>
    public bool OwnersMayCreate { get; init; } = true;

    /// <summary>How many tiles away ordinary speech reaches an AI bot; zero answers only on its tile or when named.</summary>
    public int NearTiles { get; init; } = 2;

    /// <summary>The badges an AI bot may offer to give a player, each gift approved by staff first. Empty: bots give no badge.</summary>
    public List<string> GivableBadges { get; init; } = [];
}
