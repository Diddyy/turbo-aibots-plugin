using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiBots.Bots;
using AiBots.Database;
using AiBots.Tools;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Turbo.Contracts.Ai;
using Turbo.Contracts.Hotel;
using Turbo.Contracts.Plugins;
using Turbo.Database.Delegates;
using Turbo.Primitives.Moderation;
using Turbo.Primitives.Rooms.Enums;
using Turbo.Testing;
using Xunit;

namespace AiBots.Tests;

public sealed class AiBotsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("hello", 2, 2, true)]
    [InlineData("Guide, hello", 20, 20, true)]
    [InlineData("guideline", 20, 20, false)]
    [InlineData("hello", 20, 20, false)]
    public async Task Near_speech_and_whole_name_mentions_address_one_bot(string text, int x, int y, bool answers)
    {
        await using var f = await Fixture.CreateAsync();
        f.Ai.Respond("Hello!");
        await f.Chat.HandleAsync(f.Heard(text, x, y), Ct);
        Assert.Equal(answers ? 1 : 0, f.Ai.Requests.Count);
        Assert.Equal(answers ? 1 : 0, f.Hotel.Bots.Lines.Count);
    }

    [Fact]
    public async Task Private_player_whispers_and_unknown_names_never_reach_a_model()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Chat.HandleAsync(f.Heard("Guide, secret") with { Type = RoomChatType.Whisper, TargetPlayer = f.Owner.Id, Recipient = f.Owner.Name }, Ct);
        await f.Chat.HandleAsync(f.Heard("Guide, secret") with { Type = RoomChatType.Whisper, Recipient = "Absent" }, Ct);
        Assert.Empty(f.Ai.Requests);
        Assert.Empty(f.Hotel.Bots.Lines);
    }

    [Fact]
    public async Task Bot_whispers_have_private_memory_and_never_publish_private_tool_speech()
    {
        await using var f = await Fixture.CreateAsync();
        f.Ai.CallTool("aibots_say", new { text = "a private secret", how = "say" }).Respond("Just between us.").Respond("Public hello.");
        await f.Chat.HandleAsync(f.Heard("private question") with { Type = RoomChatType.Whisper, Recipient = "Guide" }, Ct);
        Assert.Single(f.Hotel.Bots.Lines);
        Assert.Equal(f.Player.Id, f.Hotel.Bots.Lines[0].WhisperedTo);
        await f.Chat.HandleAsync(f.Heard("Guide hello"), Ct);
        Assert.Equal(2, f.Ai.Store.Conversations.Count);
        Assert.DoesNotContain(f.Ai.Requests.Last().Messages, x => x.Text.Contains("private question"));
        Assert.DoesNotContain(f.Hotel.Bots.Lines, x => x.Text.Contains("private secret"));
        await f.Management.ForgetAsync(f.Player.Id, Ct);
        Assert.Empty(f.Ai.Store.Conversations);
    }

    [Fact]
    public async Task A_bot_can_walk_and_wave_but_cannot_order_another_bot()
    {
        await using var f = await Fixture.CreateAsync();
        var other = f.Hotel.Bots.Add(f.Room, "Other", f.Owner, 4, 4);
        f.Ai.CallTool("aibots_walk_to_tile", new { x = 3, y = 3, bot = other.Id })
            .CallTool("aibots_walk_to_tile", new { x = 2, y = 2 })
            .CallTool("aibots_gesture", new { gesture = "wave" }).Respond("Hi!");
        await f.Chat.HandleAsync(f.Heard("Guide wave"), Ct);
        Assert.Equal((2, 2), (f.Bot.X, f.Bot.Y));
        Assert.Equal((4, 4), (other.X, other.Y));
        Assert.Single(f.Bot.Gestures);
    }

    [Theory]
    [InlineData("email me at a@example.com")]
    [InlineData("Call 01234567890")]
    [InlineData("https://example.com")]
    [InlineData("Rules AIBOTS-RULES-7Q2")]
    public async Task Unsafe_answers_are_replaced_before_speech_and_memory(string text)
    {
        await using var f = await Fixture.CreateAsync();
        f.Ai.Respond(text);
        await f.Chat.HandleAsync(f.Heard("Guide hello"), Ct);
        Assert.Equal(AiBotTexts.Default(AiBotTexts.REFUSED), Assert.Single(f.Hotel.Bots.Lines).Text);
        Assert.All(f.Ai.Store.Conversations, conversation => Assert.DoesNotContain(text, conversation.Messages));
    }

    [Fact]
    public async Task Replies_use_the_word_filter_and_daily_budgets_get_a_polite_fallback()
    {
        await using var f = await Fixture.CreateAsync();
        f.Ai.Settings = new() { PlayerDailyTokens = 1, PlayerRunsPerMinute = 0, PluginRunsPerMinute = 0 };
        f.Ai.Respond("forbidden word");
        await f.Chat.HandleAsync(f.Heard("Guide hello"), Ct);
        await f.Chat.HandleAsync(f.Heard("Guide again"), Ct);
        Assert.Equal("bobba word", f.Hotel.Bots.Lines[0].Text);
        Assert.Equal(AiBotTexts.Default(AiBotTexts.DAILY), f.Hotel.Bots.Lines[1].Text);
        Assert.Single(f.Ai.Requests);
    }

    [Fact]
    public async Task Owner_checks_room_switch_and_hotel_kill_switch_are_enforced()
    {
        await using var f = await Fixture.CreateAsync();
        var refused = await f.Management.ConfigureAsync(f.Player.Id, f.Room.Id, $"{f.Bot.Id} off", Ct);
        Assert.Contains("owner", refused);
        await f.Management.ConfigureAsync(f.Owner.Id, f.Room.Id, "room off", Ct);
        await f.Chat.HandleAsync(f.Heard("Guide hello"), Ct);
        Assert.Empty(f.Ai.Requests);
        await f.Management.ConfigureAsync(f.Owner.Id, f.Room.Id, "room on", Ct);
        await f.Directory.SetStoppedAsync(true, f.Owner.Id, Ct);
        await f.Chat.HandleAsync(f.Heard("Guide hello"), Ct);
        Assert.Empty(f.Ai.Requests);
    }

    [Fact]
    public async Task A_badge_waits_for_approval_and_the_resume_whispers_to_the_original_player_only()
    {
        await using var f = await Fixture.CreateAsync();
        f.Options.Value = new() { GivableBadges = ["ACH_Test"] };
        using var live = f.Ai.Live(new PluginManifest { Name = "AI bots", Key = "aibots", Version = "0.1.0", Author = "test", Ai = new() { Purpose = "tests" } }, typeof(AiBotAgent).Assembly, f.Services);
        f.Ai.CallTool("aibots_give_badge", new { player = f.Player.Name, badge = "ACH_Test" }).Respond("Your badge is ready.");
        await f.Chat.HandleAsync(f.Heard("Guide badge"), Ct);
        Assert.False(await f.Hotel.Inventory.HasBadgeAsync(f.Player.Id, "ACH_Test", Ct));
        Assert.Single(f.Ai.Approvals.Requests);
        await f.Ai.Approvals.ApproveAsync();
        f.Hotel.Inventory.AssertHasBadge(f.Player.Id, "ACH_Test");
        Assert.Equal(f.Player.Id, f.Hotel.Bots.Lines.Last().WhisperedTo);
        Assert.Equal("Your badge is ready.", f.Hotel.Bots.Lines.Last().Text);
    }

    [Fact]
    public async Task A_player_who_leaves_gets_no_delayed_approval_reply()
    {
        await using var f = await Fixture.CreateAsync();
        var otherRoom = f.Hotel.Rooms.Add("Elsewhere", f.Owner);
        f.Hotel.Rooms.Enter(otherRoom, f.Player);
        await f.Replies.ResumedAsync(new AiResumed(new AiResponse { Text = "private answer", Stop = AiStopReason.Finished }, f.Player.Id, AiBotDirectory.MemoryOf(f.Bot.Id, f.Player.Id).Participant), Ct);
        Assert.Empty(f.Hotel.Bots.Lines);
    }

    [Fact]
    public async Task The_off_turn_queue_is_bounded_and_whispers_share_a_flood_limit()
    {
        using var work = new AiBotWork(NullLogger<AiBotWork>.Instance);
        for (var i = 0; i < 128; i++) Assert.True(work.TryEnqueue(_ => Task.CompletedTask));
        Assert.False(work.TryEnqueue(_ => Task.CompletedTask));
        await using var f = await Fixture.CreateAsync();
        var bot = (await f.Directory.FindAsync(f.Bot.Id, Ct))!;
        for (var i = 0; i < 10; i++) Assert.True(await f.Speech.SayAsync(bot, f.Player.Id, "hello", true, false, Ct));
        Assert.False(await f.Speech.SayAsync(bot, f.Player.Id, "hello", true, false, Ct));
        f.Hotel.Clock.Advance(TimeSpan.FromSeconds(5));
        Assert.True(await f.Speech.SayAsync(bot, f.Player.Id, "hello", true, false, Ct));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _db = new("Data Source=:memory:");
        public FakeHotel Hotel { get; } = new();
        public FakePlayer Owner { get; }
        public FakePlayer Player { get; }
        public FakeRoom Room { get; }
        public FakeBot Bot { get; }
        public AiFake Ai { get; }
        public Monitor Options { get; } = new();
        public ServiceProvider Services { get; }
        public AiBotDirectory Directory { get; }
        public AiBotChat Chat { get; }
        public AiBotReplies Replies { get; }
        public AiBotSpeech Speech { get; }
        public AiBotManagement Management { get; }

        private Fixture()
        {
            _db.Open();
            Owner = Hotel.Players.Add("Owner", online: true);
            Player = Hotel.Players.Add("Alice", online: true);
            Room = Hotel.Rooms.Add("Lobby", Owner);
            Hotel.Rooms.Enter(Room, Owner);
            Hotel.Rooms.Enter(Room, Player);
            Bot = Hotel.Bots.Add(Room, "Guide", Owner, 1, 1);
            Ai = new AiFake(Hotel).PreventStrayPrompts();
            var api = Ai.For("aibots");
            Services = new ServiceCollection().AddSingleton<IHotel>(Hotel)
                .AddSingleton<TablePrefixProvider>(() => "aibots_")
                .AddDbContext<AiBotsDb>(options => options.UseSqlite(_db))
                .AddSingleton<IAi>(api).AddSingleton<IOptionsMonitor<AiBotsSettings>>(Options)
                .AddSingleton<IWordFilter, TestWordFilter>().AddSingleton<IAiBotDirectory, AiBotDirectory>()
                .AddSingleton<AiBotScenes>().AddSingleton<AiBotTarget>().AddSingleton<AiBotTexts>()
                .AddSingleton<AiBotSpeech>().AddSingleton<AiBotReplies>().AddSingleton<AiBotAgent>()
                .AddSingleton<AiBotChat>().AddSingleton<AiBotManagement>().BuildServiceProvider();
            Directory = (AiBotDirectory)Services.GetRequiredService<IAiBotDirectory>();
            Chat = Services.GetRequiredService<AiBotChat>();
            Replies = Services.GetRequiredService<AiBotReplies>();
            Speech = Services.GetRequiredService<AiBotSpeech>();
            Management = Services.GetRequiredService<AiBotManagement>();
        }

        public static async Task<Fixture> CreateAsync()
        {
            var f = new Fixture();
            await using var scope = f.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AiBotsDb>().Database.EnsureCreatedAsync(Ct);
            var bot = (await f.Hotel.Bots.InRoomAsync(f.Room.Id, Ct)).Single();
            await f.Directory.SaveAsync(bot, f.Owner.Id, "Friendly guide", "Catalog furniture", "English", true, Ct);
            return f;
        }

        public AiBotHeard Heard(string text, int x = 2, int y = 2) => new(Room.Id, Player.Id, text, RoomChatType.Chat, null, null, x, y);
        public async ValueTask DisposeAsync() { await Services.DisposeAsync(); await _db.DisposeAsync(); }
    }

    private sealed class Monitor : IOptionsMonitor<AiBotsSettings>
    {
        public AiBotsSettings Value { get; set; } = new();
        public AiBotsSettings CurrentValue => Value;
        public AiBotsSettings Get(string? name) => Value;
        public IDisposable? OnChange(Action<AiBotsSettings, string?> listener) => null;
    }

    private sealed class TestWordFilter : IWordFilter
    {
        public string Replacement => "bobba";
        public string Filter(string text) => text.Replace("forbidden", Replacement, StringComparison.OrdinalIgnoreCase);
        public string Filter(string text, IReadOnlySet<string> extraWords) => FilterWords.Apply(Filter(text), extraWords, Replacement);
        public bool IsClean(string text) => Filter(text) == text;
        public Task ReloadAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
