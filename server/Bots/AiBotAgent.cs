using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AiBots.Tools;
using Microsoft.Extensions.Options;
using Turbo.Contracts.Ai;
using Turbo.Contracts.Hotel;
using Turbo.Contracts.Plugins.Mcp;
using Turbo.Primitives.Moderation;

namespace AiBots.Bots;

/// <summary>A resumable agent built only from services; scene authority is limited to the bot answering now.</summary>
public sealed class AiBotAgent(IHotel hotel, AiBotScenes scenes, AiBotTarget target, IAiBotDirectory directory, IOptionsMonitor<AiBotsSettings> settings, IWordFilter filter, AiBotTexts texts, AiBotSpeech speech, AiBotReplies replies) : AiAgent
{
    public override string Instructions => AiBotPrompt.For(scenes.Current);
    public override int MaxSteps => 4;
    public override int MaxTokens => 256;
    public override TimeSpan? Timeout => TimeSpan.FromSeconds(20);
    public override AiBudget? Budget => new(Tokens: 4000);
    public override IEnumerable<IAiMiddleware> Middleware => [new AiBotModeration(filter, texts)];

    // On an approval continuation only the approved badge tool is available; no scene can be
    // inferred from model arguments and no other bot's room may be controlled.
    public override IEnumerable<IMcpTool> Tools => scenes.Current is null
        ? [new GiveBadgeTool(hotel, settings, directory)]
        : [new WalkToTileTool(hotel, target), new WalkToFurniTool(hotel, target),
           new SayTool(target, filter, speech), new GestureTool(hotel, target),
           new FindCatalogItemTool(hotel), new RoomFurniTool(hotel, target),
           new HotelTimeTool(hotel, directory), new GiveBadgeTool(hotel, settings, directory)];

    public override Task ResumedAsync(AiResumed resumed, CancellationToken ct) => replies.ResumedAsync(resumed, ct);
}
