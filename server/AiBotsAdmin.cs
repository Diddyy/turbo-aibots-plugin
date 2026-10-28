using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AiBots.Bots;
using Turbo.Contracts.Hotel;
using Turbo.Contracts.Plugins.Admin;
using Turbo.Primitives.Players;

namespace AiBots;

public sealed class AiBotsAdmin(IAiBotDirectory directory, IHotel hotel) : IPluginAdmin
{
    public void Configure(IPluginAdminBuilder admin)
    {
        var view = admin.Node("bots.view", "AI bots: see personas and usage");
        var manage = admin.Node("bots.manage", "AI bots: edit personas and stop all bots");
        admin.Map("GET", "bots", view, ListAsync);
        admin.Map("GET", "bots/{id}", manage, GetAsync);
        admin.Map("PUT", "bots/{id}", manage, SaveAsync);
        admin.Map("POST", "stop", manage, (request, ct) => StopAsync(request, true, ct));
        admin.Map("POST", "resume", manage, (request, ct) => StopAsync(request, false, ct));
        admin.Map("GET", "switch", view, async (_, ct) => PluginAdminResult.Ok(await directory.SwitchAsync(ct).ConfigureAwait(false)));
        admin.Map("PUT", "switch", manage, WhatsOnAsync);
        admin.Page(new PluginAdminPage("bots", "AI bots", view, [
            new PluginAdminBlock("detail", "Hotel switch", Endpoint: "switch", Columns: [new("stopped", "All AI bots stopped", "bool"), new("whatsOn", "What's on")]),
            new PluginAdminBlock("actions", Actions: [new("Stop every AI bot", "POST", "stop", "Stop AI bots across the hotel?", Danger: true, Node: manage), new("Resume AI bots", "POST", "resume", Node: manage)]),
            new PluginAdminBlock("table", "Personas and usage", Endpoint: "bots", ItemPage: "bot", Columns: [new("id", "Bot", "number"), new("room", "Room", "number"), new("name", "Name"), new("personality", "Personality"), new("enabled", "Enabled", "bool"), new("replies", "Replies", "number"), new("fallbacks", "Fallbacks", "number"), new("tokensIn", "Input tokens", "number"), new("tokensOut", "Output tokens", "number")]),
        ], "Provider keys and budgets are managed under Plugins → AI. No prompts or chat transcripts are listed.", Icon: "bot"));
        admin.Page(new PluginAdminPage("bot", "AI bot persona", manage, [new PluginAdminBlock("form", Endpoint: "bots/{id}", Fields: [new("personality", "Personality", "textarea", Required: true), new("knows", "What it knows", "textarea"), new("language", "Language", Required: true), new("enabled", "Enabled", "bool")], Submit: new("Save persona", "PUT", "bots/{id}"))], Nav: false));
        admin.Page(new PluginAdminPage("events", "AI bot events", manage, [new PluginAdminBlock("form", "What bots tell players is on", Endpoint: "switch", Fields: [new("whatsOn", "What's on", "textarea")], Submit: new("Save", "PUT", "switch"))]));
    }

    private async Task<PluginAdminResult> ListAsync(PluginAdminRequest request, CancellationToken ct)
    {
        var rows = await directory.AllAsync(ct).ConfigureAwait(false);
        var page = request.Query.TryGetValue("page", out var number) && int.TryParse(number, out var parsed) ? Math.Max(1, parsed) : 1;
        return PluginAdminResult.Ok(new { items = rows.Skip((page - 1) * 50).Take(50).Select(x => new { id = x.BotId, room = x.RoomId, name = x.BotName, x.Personality, x.Enabled, x.Replies, x.Fallbacks, x.TokensIn, x.TokensOut }), total = rows.Count, pageSize = 50 });
    }

    private async Task<PluginAdminResult> GetAsync(PluginAdminRequest request, CancellationToken ct) =>
        Id(request) is { } id && await directory.FindAsync(id, ct).ConfigureAwait(false) is { } bot ? PluginAdminResult.Ok(bot) : PluginAdminResult.NotFound("No such AI bot.");

    private async Task<PluginAdminResult> SaveAsync(PluginAdminRequest request, CancellationToken ct)
    {
        if (Id(request) is not { } id || await directory.FindAsync(id, ct).ConfigureAwait(false) is not { } persona) return PluginAdminResult.NotFound("No such AI bot.");
        var body = request.Body;
        var personality = Text(body, "personality");
        var knows = Text(body, "knows");
        var language = Text(body, "language");
        if (personality.Length is < 1 or > 200 || knows.Length > 300 || language.Length is < 1 or > 24)
            return PluginAdminResult.BadRequest("Personality is 1–200 characters, knowledge up to 300, language 1–24.");
        if (body is not { ValueKind: JsonValueKind.Object } json || !json.TryGetProperty("enabled", out var enabled) || enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return PluginAdminResult.BadRequest("Enabled must be true or false.");
        var bot = (await hotel.Bots.InRoomAsync(persona.Room, ct).ConfigureAwait(false)).FirstOrDefault(x => x.Id == id);
        if (bot is null) return PluginAdminResult.BadRequest("Enter the bot's room before editing its persona.");
        await directory.SaveAsync(bot, new PlayerId(request.PlayerId), personality, knows, language, enabled.GetBoolean(), ct).ConfigureAwait(false);
        return PluginAdminResult.NoContent();
    }

    private async Task<PluginAdminResult> StopAsync(PluginAdminRequest request, bool stopped, CancellationToken ct)
    {
        await directory.SetStoppedAsync(stopped, new PlayerId(request.PlayerId), ct).ConfigureAwait(false);
        return PluginAdminResult.NoContent();
    }

    private async Task<PluginAdminResult> WhatsOnAsync(PluginAdminRequest request, CancellationToken ct)
    {
        var text = Text(request.Body, "whatsOn");
        if (text.Length > 500) return PluginAdminResult.BadRequest("What's on is at most 500 characters.");
        await directory.SetWhatsOnAsync(text, ct).ConfigureAwait(false);
        return PluginAdminResult.NoContent();
    }

    private static int? Id(PluginAdminRequest request) => request.Route.TryGetValue("id", out var value) && int.TryParse(value, out var id) && id > 0 ? id : null;
    private static string Text(JsonElement? body, string name) => body is { ValueKind: JsonValueKind.Object } json && json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()!.Trim() : string.Empty;
}
