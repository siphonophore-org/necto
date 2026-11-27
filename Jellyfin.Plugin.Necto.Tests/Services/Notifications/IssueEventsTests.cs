using System;
using System.Linq;
using Jellyfin.Plugin.Necto.Services.Notifications;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Notifications;

/// <summary>
/// The Issues events are each listed once under the issues tool's key, a report being created and a decision updated.
/// </summary>
public class IssueEventsTests
{
    /// <summary>
    /// Every key is listed once, each under issues.
    /// True: Notifications lists the two Issues events once, under Issues.
    /// False: an issue event would be listed twice or under Requests.
    /// </summary>
    [Fact]
    public void All_Keys_AreEachListedOnceUnderIssues()
    {
        Assert.Equal(new[] { "issues.created", "issues.updated" }, IssueEvents.All);
        Assert.Equal(IssueEvents.All.Count, IssueEvents.All.Distinct(StringComparer.Ordinal).Count());
        Assert.All(IssueEvents.All, k => Assert.StartsWith("issues.", k, StringComparison.Ordinal));
    }

    /// <summary>
    /// A report is created and a resolution updated.
    /// True: managers can hear of reports apart from reporters hearing of decisions.
    /// False: a report and a decision would share a key.
    /// </summary>
    [Theory]
    [InlineData(NotificationEvent.IssueReported, IssueEvents.Created)]
    [InlineData(NotificationEvent.IssueResolved, IssueEvents.Updated)]
    public void KeyFor_Event_IsItsOwnKey(NotificationEvent what, string key)
        => Assert.Equal(key, IssueEvents.KeyFor(what));
}
