using System.Linq;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Requests;

/// <summary>
/// The Requests activity entries sum what happened in one run by who and where, one entry per outcome, in US English.
/// </summary>
public class RequestActivityTests
{
    private readonly FakeActivity _written = new();
    private readonly RequestActivity _activity;

    public RequestActivityTests()
    {
        _activity = new RequestActivity(_written.Logger);
    }

    /// <summary>
    /// Arrivals are summed by requester.
    /// True: one entry says how many became available and for whom.
    /// False: the log would hold an entry per title, or lose who they were for.
    /// </summary>
    [Fact]
    public void Flush_Arrivals_AreSummedByRequester()
    {
        _activity.Add(RequestActivity.Available, "alice");
        _activity.Add(RequestActivity.Available, "bob");
        _activity.Add(RequestActivity.Available, "alice");

        var entry = Only();

        Assert.Equal("Requests available", entry.Name);
        Assert.Equal("Requests.Available", entry.Type);
        Assert.Equal("3 requests became available for alice (2), bob", entry.ShortOverview);
        Assert.Equal(LogLevel.Information, entry.LogSeverity);
    }

    /// <summary>
    /// Losses are summed with whether all or part of each went away.
    /// True: the entry says a title left the library, wholly or partly.
    /// False: a partly gone show would read as wholly gone.
    /// </summary>
    [Fact]
    public void Flush_Losses_SayAllOrPart()
    {
        _activity.Add(RequestActivity.Unavailable, "alice", "gone");
        _activity.Add(RequestActivity.Unavailable, "bob", "partly gone");

        var entry = Only();

        Assert.Equal("Requests unavailable", entry.Name);
        Assert.Equal("Requests.Unavailable", entry.Type);
        Assert.Equal("2 requests left the library for alice, bob: gone, partly gone", entry.ShortOverview);
    }

    /// <summary>
    /// Retries are summed by server.
    /// True: the entry names the server a retried request reached.
    /// False: a retry would be logged without where it went.
    /// </summary>
    [Fact]
    public void Flush_Retries_AreSummedByServer()
    {
        _activity.Add(RequestActivity.SentOnRetry, "Radarr");

        var entry = Only();

        Assert.Equal("Request sent on retry", entry.Name);
        Assert.Equal("Requests.Approved", entry.Type);
        Assert.Equal("1 request was sent on retry to Radarr", entry.ShortOverview);
    }

    /// <summary>
    /// Failed sends are a warning, summed by server and error.
    /// True: an administrator sees which server refused what, flagged as a warning.
    /// False: a failure would read as routine, or repeat once per request.
    /// </summary>
    [Fact]
    public void Flush_FailedSends_AreAWarning()
    {
        _activity.Add(RequestActivity.NotSent, "Sonarr", "Sonarr is unreachable.");
        _activity.Add(RequestActivity.NotSent, "Sonarr", "Sonarr is unreachable.");

        var entry = Only();

        Assert.Equal("Requests not sent", entry.Name);
        Assert.Equal("Requests.Failed", entry.Type);
        Assert.Equal("2 requests could not be sent to Sonarr (2): Sonarr is unreachable (2)", entry.ShortOverview);
        Assert.Equal(LogLevel.Warning, entry.LogSeverity);
    }

    /// <summary>
    /// Each outcome is its own entry.
    /// True: an arrival and a failure in one run are two entries.
    /// False: different outcomes would be merged into one misleading entry.
    /// </summary>
    [Fact]
    public void Flush_TwoOutcomes_AreTwoEntries()
    {
        _activity.Add(RequestActivity.Available, "alice");
        _activity.Add(RequestActivity.NotSent, "Radarr");

        _activity.Flush();

        Assert.Equal(new[] { "Request available", "Request not sent" }, _written.Names.Order().ToArray());
    }

    /// <summary>
    /// A bulk run's overview names who acted and whose requests, with the count skipped, and an expiry names the wait.
    /// True: the log says who rejected which requests and how many were left alone.
    /// False: a bulk action would be logged without who did it or to whom.
    /// </summary>
    [Fact]
    public void RunOverview_BulkRun_NamesWhoAndWhose()
    {
        Assert.Equal("3 requests were rejected by admin for alice (2), bob, with 1 other skipped", RequestActivity.RunOverview("decline", "admin", new[] { "alice", "bob", "alice" }, 1));
        Assert.Equal("1 request was rejected after waiting 30 days for alice", RequestActivity.ExpiredOverview(30, new[] { "alice" }));
        Assert.Equal("Requests approved", RequestActivity.RunName("approve", 2));
        Assert.Equal("Request removed", RequestActivity.RunName("remove", 1));
    }

    private Jellyfin.Database.Implementations.Entities.ActivityLog Only()
    {
        _activity.Flush();
        return Assert.Single(_written.Entries);
    }
}
