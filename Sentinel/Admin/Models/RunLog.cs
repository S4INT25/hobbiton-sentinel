using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Sentinel.Admin.Models;

[Table("run_logs")]
public class RunLog
{
    [Column("run_id")] public string RunId { get; set; } = "";
    [Column("iteration")] public ushort Iteration { get; set; }
    [Column("tool_name")] public string ToolName { get; set; } = "";
    [Column("args")] public string Args { get; set; } = "";
    [Column("result")] public string Result { get; set; } = "";
    [Column("started_at")] public DateTime StartedAt { get; set; }
    [Column("duration_ms")] public uint DurationMs { get; set; }
}
