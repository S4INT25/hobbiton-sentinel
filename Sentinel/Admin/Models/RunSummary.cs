using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Sentinel.Admin.Models;

[Table("run_summaries")]
public class RunSummary
{
    [Column("run_id")] public string RunId { get; set; } = "";
    [Column("started_at")] public DateTime StartedAt { get; set; }
    [Column("finished_at")] public DateTime FinishedAt { get; set; }
    [Column("iterations")] public ushort Iterations { get; set; }
    [Column("input_tokens")] public uint InputTokens { get; set; }
    [Column("output_tokens")] public uint OutputTokens { get; set; }
    [Column("cases_created")] public ushort CasesCreated { get; set; }
    [Column("cases_resolved")] public ushort CasesResolved { get; set; }
    [Column("alerts_sent")] public ushort AlertsSent { get; set; }
    [Column("status")] public string Status { get; set; } = "completed"; // completed, incomplete, error
    [Column("triggered_by")] public string TriggeredBy { get; set; } = "scheduler";
}
