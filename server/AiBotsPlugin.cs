using Microsoft.Extensions.DependencyInjection;
using AiBots.Bots;
using AiBots.Tools;
using Turbo.Contracts.Plugins;
using Turbo.Contracts.Plugins.Admin;

namespace AiBots;

/// <summary>
/// The plugin's services, for its routes, tasks, commands and handlers to take by constructor. The
/// rest is found as it loads: its key and version in manifest.json, its settings, commands,
/// permission nodes, messages, handlers, routes and tasks in the assembly, and its client module,
/// built by the hotel from client/src. All of it goes when it unloads.
/// </summary>
public sealed class AiBotsPlugin : TurboPlugin
{
    protected override void Services(IServiceCollection services)
    {
        services.AddSingleton<IAiBotDirectory, AiBotDirectory>();
        services.AddSingleton<AiBotScenes>();
        services.AddSingleton<AiBotTarget>();
        services.AddSingleton<AiBotTexts>();
        services.AddSingleton<AiBotSpeech>();
        services.AddSingleton<AiBotReplies>();
        services.AddSingleton<AiBotAgent>();
        services.AddSingleton<AiBotChat>();
        services.AddSingleton<AiBotManagement>();
        services.AddSingleton<AiBotWork>();
        services.AddHostedService(sp => sp.GetRequiredService<AiBotWork>());
        services.AddSingleton<IPluginAdmin, AiBotsAdmin>();
    }
}
