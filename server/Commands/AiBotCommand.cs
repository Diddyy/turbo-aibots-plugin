using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AiBots.Bots;
using Turbo.Contracts.Hotel;
using Turbo.Primitives.Commands;
using Turbo.Primitives.Players.Permissions;

namespace AiBots.Commands;

public sealed record AiBotArguments(RestOfLine Arguments);

[Command("aibot", Description = "Configure an AI bot in your room")]
[RequiresPermission(AiBotsPermissions.COMMAND)]
public sealed class AiBotCommand(AiBotWork work, AiBotManagement management, IHotel hotel) : ICommand<AiBotArguments>
{
    public IReadOnlyDictionary<string, string> DefaultTexts { get; } = new Dictionary<string, string> { ["busy"] = "AI bot work is busy; try again in a moment." };

    public ValueTask<CommandResult> ExecuteAsync(ICommandContext ctx, AiBotArguments arguments, CancellationToken ct)
    {
        var player = ctx.Executor.PlayerId;
        var room = ctx.RoomId;
        var text = arguments.Arguments.Text;
        var queued = work.TryEnqueue(async token => {
            var result = await management.ConfigureAsync(player, room, text, token).ConfigureAwait(false);
            await hotel.Rooms.WhisperAsync(room, player, result, token).ConfigureAwait(false);
        });
        return ValueTask.FromResult(queued ? CommandResult.Ok : CommandResult.Fail("busy"));
    }
}

[Command("forgetbots", Description = "Delete your conversations with AI bots")]
[RequiresPermission(AiBotsPermissions.FORGET)]
public sealed class ForgetBotsCommand(AiBotWork work, AiBotManagement management, IHotel hotel) : ICommand<NoArguments>
{
    public IReadOnlyDictionary<string, string> DefaultTexts { get; } = new Dictionary<string, string> { ["busy"] = "AI bot work is busy; try again in a moment." };

    public ValueTask<CommandResult> ExecuteAsync(ICommandContext ctx, NoArguments arguments, CancellationToken ct)
    {
        var player = ctx.Executor.PlayerId;
        var room = ctx.RoomId;
        var queued = work.TryEnqueue(async token => {
            await management.ForgetAsync(player, token).ConfigureAwait(false);
            await hotel.Rooms.WhisperAsync(room, player, "Your AI bot conversations were forgotten.", token).ConfigureAwait(false);
        });
        return ValueTask.FromResult(queued ? CommandResult.Ok : CommandResult.Fail("busy"));
    }
}
