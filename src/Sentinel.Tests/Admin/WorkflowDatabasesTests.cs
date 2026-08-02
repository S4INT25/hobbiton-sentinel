using Sentinel.Admin.Models;

namespace Sentinel.Tests.Admin;

/// <summary>
/// Multi-database targeting is stored as one comma-separated column, so parsing is the whole
/// feature. Order matters — the first entry is the agent's primary database and supplies the
/// default schema, so a parser that reorders would silently retarget every existing workflow.
/// </summary>
public class WorkflowDatabasesTests
{
    [Fact]
    public void Existing_single_database_rows_still_work()
    {
        Assert.Equal(["lipila_blaze"], WorkflowDatabases.Parse("lipila_blaze"));
        Assert.Equal("lipila_blaze", WorkflowDatabases.Primary("lipila_blaze"));
        Assert.Empty(WorkflowDatabases.Secondary("lipila_blaze"));
    }

    [Fact]
    public void Parse_preserves_order_and_trims()
    {
        Assert.Equal(
            ["inshuwa", "lipila_blaze", "bnpl"],
            WorkflowDatabases.Parse(" inshuwa , lipila_blaze ,bnpl "));
    }

    [Fact]
    public void Parse_accepts_semicolons_and_drops_blanks()
    {
        Assert.Equal(["a", "b"], WorkflowDatabases.Parse("a;;b,"));
    }

    [Fact]
    public void Parse_dedupes_case_insensitively_keeping_the_first_spelling()
    {
        Assert.Equal(["Inshuwa", "bnpl"], WorkflowDatabases.Parse("Inshuwa,inshuwa,bnpl,BNPL"));
    }

    [Fact]
    public void Primary_is_the_first_entry_and_secondary_is_the_rest()
    {
        const string dbs = "inshuwa,lipila_blaze,bnpl";
        Assert.Equal("inshuwa", WorkflowDatabases.Primary(dbs));
        Assert.Equal(["lipila_blaze", "bnpl"], WorkflowDatabases.Secondary(dbs));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",")]
    public void Empty_targets_fall_back_rather_than_throwing(string? value)
    {
        Assert.Empty(WorkflowDatabases.Parse(value));
        Assert.Equal("lipila_blaze", WorkflowDatabases.Primary(value));
        Assert.Equal("custom_default", WorkflowDatabases.Primary(value, "custom_default"));
        Assert.Empty(WorkflowDatabases.Secondary(value));
    }

    [Fact]
    public void Normalize_round_trips_and_is_idempotent()
    {
        var once = WorkflowDatabases.Normalize(" inshuwa , bnpl ,inshuwa ");
        Assert.Equal("inshuwa,bnpl", once);
        Assert.Equal(once, WorkflowDatabases.Normalize(once));
    }
}
