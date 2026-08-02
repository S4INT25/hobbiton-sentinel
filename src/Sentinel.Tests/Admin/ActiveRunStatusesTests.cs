using Sentinel.Admin;

namespace Sentinel.Tests.Admin;

/// <summary>
/// Failed and stopped runs stay in the tracker for their 24-hour TTL so the detail page can still
/// say how a run ended. Treating mere presence as "active" made a workflow advertise "Running"
/// for a day after it died, which is worse than showing nothing.
/// </summary>
public class ActiveRunStatusesTests
{
    [Theory]
    [InlineData("queued")]
    [InlineData("running")]
    public void In_flight_statuses_are_live(string status)
    {
        Assert.True(ActiveRunStatuses.IsInFlight(status));
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("stopped")]
    [InlineData("completed")]
    [InlineData("error")]
    [InlineData("")]
    [InlineData(null)]
    public void Terminal_and_unknown_statuses_are_not_live(string? status)
    {
        Assert.False(ActiveRunStatuses.IsInFlight(status));
    }

    [Fact]
    public void Status_constants_match_what_the_tracker_writes()
    {
        // MarkQueuedAsync / MarkRunningAsync write these literals; a rename on one side only
        // would silently stop every run from ever looking live.
        Assert.Equal("queued", ActiveRunStatuses.Queued);
        Assert.Equal("running", ActiveRunStatuses.Running);
    }
}
