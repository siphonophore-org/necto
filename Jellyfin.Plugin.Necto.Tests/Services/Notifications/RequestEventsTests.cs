using System;
using System.Linq;
using Jellyfin.Plugin.Necto.Services.Notifications;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Notifications;

/// <summary>
/// The Requests events are each listed once under the requests tool's key, and each request event has its own key.
/// </summary>
public class RequestEventsTests
{
    /// <summary>
    /// Every key is listed once, in the order a request lives through them, each under requests.
    /// True: Notifications lists every Requests event once, under Requests.
    /// False: an event would be listed twice, under another tool, or not at all.
    /// </summary>
    [Fact]
    public void All_Keys_AreEachListedOnceUnderRequests()
    {
        Assert.Equal(new[] { "requests.created", "requests.approved", "requests.declined", "requests.available", "requests.failed", "requests.aging", "requests.expiring" }, RequestEvents.All);
        Assert.Equal(RequestEvents.All.Count, RequestEvents.All.Distinct(StringComparer.Ordinal).Count());
        Assert.All(RequestEvents.All, k => Assert.StartsWith("requests.", k, StringComparison.Ordinal));
    }

    /// <summary>
    /// Each request event has its own key, and anything else is a failure.
    /// True: an approval is sent as requests.approved and a refusal as requests.failed.
    /// False: an event would be sent under another's key, and reach the wrong people.
    /// </summary>
    [Theory]
    [InlineData(NotificationEvent.Created, RequestEvents.Created)]
    [InlineData(NotificationEvent.Approved, RequestEvents.Approved)]
    [InlineData(NotificationEvent.Declined, RequestEvents.Declined)]
    [InlineData(NotificationEvent.Available, RequestEvents.Available)]
    [InlineData(NotificationEvent.Failed, RequestEvents.Failed)]
    [InlineData(NotificationEvent.Aging, RequestEvents.Aging)]
    [InlineData(NotificationEvent.Expiring, RequestEvents.Expiring)]
    public void KeyFor_Event_IsItsOwnKey(NotificationEvent what, string key)
        => Assert.Equal(key, RequestEvents.KeyFor(what));

    /// <summary>
    /// Every request event maps to a listed key.
    /// True: no event is sent under a key Notifications does not list.
    /// False: an event would be sent that no group can choose.
    /// </summary>
    [Fact]
    public void KeyFor_EveryRequestEvent_IsListed()
    {
        var requestEvents = Enum.GetValues<NotificationEvent>().Where(e => e is not NotificationEvent.IssueReported and not NotificationEvent.IssueResolved);

        Assert.All(requestEvents, e => Assert.Contains(RequestEvents.KeyFor(e), RequestEvents.All));
    }
}
