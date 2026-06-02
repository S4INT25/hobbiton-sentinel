using System.Text.Json.Serialization;

namespace Sentinel.Admin.Models;

public class EvidenceSource
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string EvidenceDatabase { get; set; } = "";

    /// <summary>Comma-separated Lipila merchant IDs this source covers (e.g. "35" or "804,805,821").</summary>
    public string LipilaMerchantIds { get; set; } = "";

    /// <summary>Optional Lipila partner_id (e.g. 1 for Inshuwa). 0 means not partner-based.</summary>
    public int LipilaPartnerId { get; set; }

    /// <summary>JSON array of join mappings: [{lipilaTable, lipilaColumn, evidenceTable, evidenceColumn}]</summary>
    public string JoinMappings { get; set; } = "[]";

    /// <summary>JSON describing available tables and their columns for the agent prompt.</summary>
    public string TableDescriptions { get; set; } = "";

    /// <summary>JSON array of evidence check descriptions the agent should run.</summary>
    public string EvidenceChecks { get; set; } = "[]";

    /// <summary>Free text notes/context injected into the agent prompt.</summary>
    public string Notes { get; set; } = "";

    /// <summary>Optional workflow scope. Null/empty = global (available to all workflows).</summary>
    public string? WorkflowId { get; set; }

    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = "system";
}
