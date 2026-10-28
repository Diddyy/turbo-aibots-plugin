using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Turbo.Database.Context;
using Turbo.Database.Delegates;

namespace AiBots.Database;

/// <summary>The plugin's tables, named with its prefix (<c>aibots_</c>).</summary>
public sealed class AiBotsDb : PluginDbContextBase<AiBotsDb>
{
    public AiBotsDb() { }

    public AiBotsDb(DbContextOptions<AiBotsDb> options, TablePrefixProvider prefix)
        : base(options, prefix) { }

    public DbSet<AiBotRow> Bots => Set<AiBotRow>();

    public DbSet<AiRoomOffRow> RoomsOff => Set<AiRoomOffRow>();

    public DbSet<AiSwitchRow> Switch => Set<AiSwitchRow>();
}

/// <summary>A bot made an AI bot: its persona, who made it, and what it has used.</summary>
[Table("bots")]
public sealed class AiBotRow
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int BotId { get; set; }

    public int RoomId { get; set; }

    [MaxLength(64)]
    public string BotName { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Personality { get; set; } = string.Empty;

    [MaxLength(300)]
    public string Knows { get; set; } = string.Empty;

    [MaxLength(24)]
    public string Language { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public int CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public int Replies { get; set; }

    public int Fallbacks { get; set; }

    public long TokensIn { get; set; }

    public long TokensOut { get; set; }

    public DateTime? LastReplyAt { get; set; }
}

/// <summary>A room whose owner turned its AI bots off.</summary>
[Table("rooms_off")]
public sealed class AiRoomOffRow
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int RoomId { get; set; }

    public int By { get; set; }

    public DateTime At { get; set; }
}

/// <summary>The panel's kill switch and the "what's on" the bots tell players: one row.</summary>
[Table("switch")]
public sealed class AiSwitchRow
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Id { get; set; } = 1;

    public bool Stopped { get; set; }

    public int StoppedBy { get; set; }

    public DateTime? StoppedAt { get; set; }

    [MaxLength(1000)]
    public string WhatsOn { get; set; } = string.Empty;
}
