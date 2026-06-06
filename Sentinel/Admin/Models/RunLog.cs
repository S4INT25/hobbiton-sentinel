using System.ComponentModel.DataAnnotations.Schema;

namespace Sentinel.Admin.Models;

[Table("run_logs")]
public class RunLog
{
    [Column("id")] public long Id { get; set; }
    [Column("run_id")] public string RunId { get; set; } = "";
    [Column("iteration")] public ushort Iteration { get; set; }
    [Column("tool_name")] public string ToolName { get; set; } = "";
    [Column("args")] public string Args { get; set; } = "";
    [Column("result")] public string Result { get; set; } = "";
    [Column("started_at")] public DateTimeOffset StartedAt { get; set; }
    [Column("duration_ms")] public uint DurationMs { get; set; }

    /// <summary>
    /// Discriminator: "tool_call" (default) or "message" for conversation history entries.
    /// Message entries use ToolName as role (system/user/assistant) and Result as content.
    /// </summary>
    [Column("log_type")]
    public string LogType { get; set; } = "tool_call";
}