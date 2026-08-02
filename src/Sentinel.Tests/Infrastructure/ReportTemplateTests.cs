using Sentinel.Infrastructure;

namespace Sentinel.Tests.Infrastructure;

/// <summary>
/// Guards the report-template routing: the wrong template means an executive digest
/// arrives styled (and inbox-prioritised) as an incident, or vice versa.
/// </summary>
public class ReportTemplateTests
{
    [Theory]
    [InlineData("executive", "executive")]
    [InlineData("operational", "operational")]
    [InlineData("incident", "incident")]
    [InlineData("activity", "activity")]
    [InlineData("custom", "custom")]
    [InlineData("  Incident  ", "incident")]
    [InlineData("ACTIVITY", "activity")]
    public void Resolve_maps_known_keys(string key, string expected)
    {
        Assert.Equal(expected, ReportTemplate.Resolve(key, "watching").Key);
    }

    [Theory]
    [InlineData("fraud_alert", "incident")]
    [InlineData("insights", "executive")]
    public void Resolve_maps_legacy_keys(string legacy, string expected)
    {
        Assert.Equal(expected, ReportTemplate.Resolve(legacy, "watching").Key);
    }

    [Fact]
    public void Unlabelled_critical_report_is_treated_as_an_incident()
    {
        Assert.Equal("incident", ReportTemplate.Resolve(null, "critical").Key);
        Assert.Equal("incident", ReportTemplate.Resolve("", "critical").Key);
        Assert.Equal("incident", ReportTemplate.Resolve("nonsense", "critical").Key);
    }

    [Fact]
    public void Unlabelled_non_critical_report_falls_back_to_custom()
    {
        Assert.Equal("custom", ReportTemplate.Resolve(null, "watching").Key);
    }

    [Fact]
    public void Only_incidents_claim_inbox_priority()
    {
        // A scheduled digest flagged "urgent" trains people to ignore the flag.
        Assert.True(ReportTemplate.Incident.HighPriority);
        Assert.False(ReportTemplate.Executive.HighPriority);
        Assert.False(ReportTemplate.Operational.HighPriority);
        Assert.False(ReportTemplate.Activity.HighPriority);
        Assert.False(ReportTemplate.Custom.HighPriority);
    }

    [Fact]
    public void Every_template_has_a_distinct_accent_and_kicker()
    {
        ReportTemplate[] all =
        [
            ReportTemplate.Executive, ReportTemplate.Operational,
            ReportTemplate.Incident, ReportTemplate.Activity, ReportTemplate.Custom
        ];

        Assert.Equal(all.Length, all.Select(t => t.Accent).Distinct().Count());
        Assert.Equal(all.Length, all.Select(t => t.Kicker).Distinct().Count());
        Assert.All(all, t => Assert.False(string.IsNullOrWhiteSpace(t.CtaLabel)));
    }
}
