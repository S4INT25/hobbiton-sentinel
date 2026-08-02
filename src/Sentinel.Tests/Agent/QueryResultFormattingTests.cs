using Sentinel.Agent;

namespace Sentinel.Tests.Agent;

/// <summary>
/// Every tool result stays in the agent's message list for the rest of the run, so an unbounded
/// result is re-sent on every subsequent iteration. These pin the size cap and, more importantly,
/// that truncation is never silent — an agent that cannot see it lost rows will report a total
/// that is simply wrong.
/// </summary>
public class QueryResultFormattingTests
{
    private static TableData Rows(int count) => new()
    {
        Columns = ["id", "amount"],
        Rows = Enumerable.Range(1, count)
            .Select(i => new Dictionary<string, string> { ["id"] = $"{i}", ["amount"] = "1234.56" })
            .ToList()
    };

    [Fact]
    public void Small_results_are_returned_whole()
    {
        var output = AnalyticsAgentCore.FormatQueryResult(1, Rows(3), maxChars: 10_000);

        Assert.Contains("Query 1 (3 rows)", output);
        Assert.Contains("id | amount", output);
        Assert.DoesNotContain("truncated", output);
        foreach (var i in new[] { "1", "2", "3" })
            Assert.Contains($"{i} | 1234.56", output);
    }

    [Fact]
    public void Oversized_results_are_capped()
    {
        var output = AnalyticsAgentCore.FormatQueryResult(1, Rows(5000), maxChars: 2000);

        // The cap is applied before appending, so the notice can push it slightly over.
        Assert.True(output.Length < 3000, $"Result was not capped: {output.Length} chars.");
    }

    [Fact]
    public void Truncation_is_stated_with_both_counts()
    {
        var output = AnalyticsAgentCore.FormatQueryResult(1, Rows(5000), maxChars: 2000);

        Assert.Contains("truncated", output);
        Assert.Contains("of 5000 rows", output);
        // The header keeps the true row count so the agent cannot mistake the shown rows for all.
        Assert.Contains("Query 1 (5000 rows)", output);
    }

    [Fact]
    public void At_least_one_row_survives_an_absurdly_small_budget()
    {
        // A single row wider than the whole budget must still come back — returning only a
        // truncation notice tells the agent nothing and it cannot recover by re-querying smaller.
        var output = AnalyticsAgentCore.FormatQueryResult(1, Rows(10), maxChars: 1);

        Assert.Contains("1 | 1234.56", output);
        Assert.Contains("showing 1 of 10 rows", output);
    }

    [Fact]
    public void Empty_results_do_not_claim_truncation()
    {
        var output = AnalyticsAgentCore.FormatQueryResult(2, new TableData(), maxChars: 10_000);

        Assert.Contains("Query 2 (0 rows)", output);
        Assert.DoesNotContain("truncated", output);
    }
}
