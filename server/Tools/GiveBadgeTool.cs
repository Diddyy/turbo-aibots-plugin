using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiBots.Bots;
using Microsoft.Extensions.Options;
using Turbo.Contracts.Hotel;
using Turbo.Contracts.Plugins.Mcp;
using Turbo.Primitives.Players.Permissions;

namespace AiBots.Tools;

public sealed record GiveBadgeInput(
    [Description("The player's name.")] [StringLength(32, MinimumLength = 1)] string Player,
    [Description("The badge's code, one of the givable badges.")] [StringLength(32, MinimumLength = 1)] string Badge
);

public sealed record BadgeGiven(string Player, string Badge, bool New);

/// <summary>
/// Gives a player one of the hotel's givable badges (<see cref="AiBotsSettings.GivableBadges"/>).
/// It changes the hotel, so a staff member approves each call in the panel's Approvals inbox first;
/// the bot's answer goes on once they decide.
/// </summary>
[RequiresPermission(AiBotsPermissions.GIVE_BADGE)]
public sealed class GiveBadgeTool(IHotel hotel, IOptionsMonitor<AiBotsSettings> settings, IAiBotDirectory directory)
    : McpTool<GiveBadgeInput, BadgeGiven>
{
    public override string Name => "aibots_give_badge";

    public override string Description =>
        "Gives a player a badge once a staff member approves it (the hotel asks them; you are told when they decide). Badges you may give: "
        + (settings.CurrentValue.GivableBadges is { Count: > 0 } badges ? string.Join(", ", badges) : "none")
        + ".";

    public override bool RequiresApproval => true;

    public override async Task<McpToolResult> HandleAsync(GiveBadgeInput input, McpToolContext ctx, CancellationToken ct)
    {
        if (!settings.CurrentValue.Enabled || (await directory.SwitchAsync(ct).ConfigureAwait(false)).Stopped)
            return McpToolResult.Error("AI bots are stopped; no badge was given.");
        var givable = settings.CurrentValue.GivableBadges;
        var badge = givable.FirstOrDefault(x => string.Equals(x, input.Badge.Trim(), StringComparison.OrdinalIgnoreCase));

        if (badge is null)
            return McpToolResult.Error(
                $"\"{input.Badge}\" is not a badge AI bots may give. Givable: {(givable.Count > 0 ? string.Join(", ", givable) : "none")}."
            );

        if (await hotel.Players.FindAsync(input.Player.Trim(), ct).ConfigureAwait(false) is not { } player)
            return McpToolResult.Error($"No player is called \"{input.Player}\".");

        var given = await hotel.Inventory.GiveBadgeAsync(player.Id, badge, ct).ConfigureAwait(false);

        return Ok(
            new BadgeGiven(player.Name, badge, given),
            given ? $"{player.Name} got the badge {badge}." : $"{player.Name} already had the badge {badge}."
        );
    }
}
