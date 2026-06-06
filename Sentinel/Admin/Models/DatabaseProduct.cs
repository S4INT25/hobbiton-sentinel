using System.ComponentModel.DataAnnotations.Schema;

namespace Sentinel.Admin.Models;

/// <summary>
/// A ClickHouse database that admins have made available for analytics chat.
/// Replaces the hardcoded AllowedDatabases list — admins can add, enable, disable,
/// and label databases from the UI without code changes.
/// </summary>
[Table("database_products")]
public class DatabaseProduct
{
    [Column("id")] public int Id { get; set; }

    /// <summary>The actual ClickHouse database name (e.g. "lipila_blaze").</summary>
    [Column("database_name")]
    public string DatabaseName { get; set; } = "";

    /// <summary>Human-friendly label shown in the UI (e.g. "Lipila Payments").</summary>
    [Column("display_name")]
    public string DisplayName { get; set; } = "";

    /// <summary>Short description of what this database contains.</summary>
    [Column("description")]
    public string? Description { get; set; }

    /// <summary>Only enabled products appear in the chat database selector.</summary>
    [Column("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>Display order in the dropdown (lower = first).</summary>
    [Column("sort_order")]
    public int SortOrder { get; set; }

    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [Column("updated_at")] public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}