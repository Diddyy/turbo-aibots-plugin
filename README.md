# AI bots

AI bots is a Turbo server plugin that gives an existing room bot an owner-written persona. Players
can talk to a bot in the room; it answers in chat and can walk, gesture, look up room furniture and
catalog prices, and check the hotel's time and staff-posted events. The plugin uses Turbo's AI
service; it does not contain provider credentials or call a provider directly. AS3 is the behavioral
source of truth.

## Requirements

- A Turbo build with the plugin SDK and AI service APIs used by this plugin.
- The hotel's AI service configured with a provider and budgets. Allow the `aibots` plugin in
  `Turbo:Ai:Plugins` and configure the hotel's AI provider/key under `Turbo:Ai`.
- The plugin's database migrations applied by Turbo when the plugin is loaded.

The manifest declares why the plugin uses AI. If the plugin is not allowed or no provider is
available, bots use the configured fallback texts.

## Build and local development

Use the repository's supported .NET SDK and set `TurboBin` to the Turbo output directory that
contains `plugin-sdk/Turbo.Plugin.props`. For example:

```powershell
$env:TurboBin = '<Turbo output directory>'
npm run check
npm test
```

For a development hotel, use `npm run dev` and add the generated `dist/aibots` directory to the
hotel's `Turbo:Plugin:DevPluginPaths`. The hotel compiles the plugin sources and loads the plugin;
its client does not need a custom module. Do not ship build output or assemblies in the plugin
folder. `npm run build` copies and checks the plugin once; `npm run package` creates a source
package for the panel.

## Configure and use

1. Load the plugin and grant the `aibots.command.aibot` permission to the ranks that may create
   personas. By default, room owners can manage bots in their own rooms. Grant
   `aibots.manage.any` to staff who may manage bots in any room.
2. In a room with a bot, use `:aibot` to list bots. Set one up with commands such as:

   ```text
   :aibot 42 personality A friendly guide who loves the hotel's old stories.
   :aibot 42 knows The room owner's collection is displayed here.
   :aibot 42 language English
   :aibot 42 on
   ```

   Replace `42` with the bot id shown by `:aibot`. Use `:aibot 42 off` to pause one bot,
   `:aibot room off` to pause all AI bots in that room, or `:aibot 42 remove` to return it to a
   plain bot and delete its persona.
3. Players can address a bot by name in ordinary room chat, speak within the configured `NearTiles`
   distance, or whisper to the bot by name. Private whispers to other players are ignored. Nearby
   selection uses the closest eligible bot; naming one selects it directly.
4. Players can use `:forgetbots` to delete their saved conversations with all AI bots.

Room owners can turn off their room's AI bots. Staff can manage hotel-wide behavior in the plugin's
admin page: stop or resume all AI bots, edit a persona, and update the hotel's “what's on” text.
That text is only included when a bot checks the hotel time/events tool. The admin page shows
persona and aggregate usage counters, not prompts or chat transcripts. Provider keys, daily token
limits, and run limits are managed by Turbo's AI settings.

## Safety and limits

- Each conversation is scoped to one bot and one player. Public and private conversations use
  separate memory. Memory is held by Turbo's AI service; `:forgetbots` requests deletion.
- Bot replies are limited by a four-step run, 256 generated tokens, a 20-second timeout, and a
  4,000-token budget. The hotel's per-player daily token and rate limits also apply.
- Replies and tool speech pass through the hotel's word filter and output guard. The guard refuses
  instruction leakage, email addresses, phone-like numbers and links, and caps a line at 200
  characters. Bot speech also has a shared per-bot flood limit.
- Tool actions are limited to the bot and room in the active conversation. Bots cannot act as
  another bot. Furniture and prices come from hotel lookups; bots are instructed not to invent
  them.
- Badge codes must be listed in the `AiBots` setting `GivableBadges`. Every badge request requires
  staff approval in the hotel's AI approvals inbox before the badge is granted. An empty list means
  bots cannot request badges.
- The panel kill switch stops new conversations hotel-wide. A setting `Enabled = false` also stops
  AI bot responses. Room-level and per-bot switches are independent.
- Fallback texts are in `gamedata/external_texts.txt`; staff can customize them through the hotel's
  text system.

## Checks

Run `npm run check` after server changes and `npm test` for the server test suite. The tests use
Turbo's `FakeHotel` and `AiFake`; they do not make provider calls or require a running hotel.
Live verification requires a configured development hotel with the plugin loaded and the AI
provider enabled.
