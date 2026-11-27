using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Services.Issues.Models;
using Jellyfin.Plugin.Necto.Services.Notifications;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Requests;

/// <summary>
/// Each request and issue event becomes one Necto event: its key, its title and body filled in the server's language,
/// the requester or first reporter it is about, the page it opens, its level, and the values a webhook carries.
/// Sending reads the loaded plugin's settings, so it runs on its own.
/// </summary>
[Collection(ServicesCollection.Name)]
public class RequestNotifierTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// A request's event has its key, its title and body filled, and is about the requester.
    /// True: Notifications sends Approved: Dune (2021) to the one who asked.
    /// False: an event would carry a template, the wrong key, or no one to send it to.
    /// </summary>
    [Fact]
    public void RequestEvent_Approved_IsFilledAndAboutTheRequester()
    {
        var request = Dune();

        var made = RequestNotifier.RequestEvent(NotificationEvent.Approved, request, 0, Now);

        Assert.Equal(RequestEvents.Approved, made.Key);
        Assert.Equal("Approved: Dune (2021)", made.Title);
        Assert.Equal("Dune (2021) was approved.", made.Body);
        Assert.Equal(request.UserId, made.UserId);
        Assert.Equal(NectoEventLevel.Info, made.Level);
    }

    /// <summary>
    /// A request's event opens the title in the library when it is there, and the request page when not.
    /// True: a notification about an arrival opens the title itself.
    /// False: a notification would open a page that does not show the title.
    /// </summary>
    [Fact]
    public void RequestEvent_Url_IsTheTitleOrTheRequestPage()
    {
        var request = Dune();
        var inLibrary = Dune();
        inLibrary.LibraryItemId = Guid.Parse("22222222222222222222222222222222");

        Assert.Equal("requests/", RequestNotifier.RequestEvent(NotificationEvent.Created, request, 0, Now).Url);
        Assert.Equal("web/#/details?id=22222222222222222222222222222222", RequestNotifier.RequestEvent(NotificationEvent.Available, inLibrary, 0, Now).Url);
    }

    /// <summary>
    /// A failure is an error, a reminder a warning, and the rest information.
    /// True: a channel can colour or skip events by how much they matter.
    /// False: a failed request would read as routine.
    /// </summary>
    [Theory]
    [InlineData(NotificationEvent.Failed, NectoEventLevel.Error)]
    [InlineData(NotificationEvent.Aging, NectoEventLevel.Warning)]
    [InlineData(NotificationEvent.Expiring, NectoEventLevel.Warning)]
    [InlineData(NotificationEvent.Created, NectoEventLevel.Info)]
    [InlineData(NotificationEvent.Declined, NectoEventLevel.Info)]
    public void RequestEvent_Level_FollowsWhatHappened(NotificationEvent what, NectoEventLevel level)
        => Assert.Equal(level, RequestNotifier.RequestEvent(what, Dune(), 10, Now).Level);

    /// <summary>
    /// A request's data carries the event, the request, its status, every joiner's id, and each provider id.
    /// True: a webhook knows which request and title an event is about, and who follows it.
    /// False: a webhook would get only words, with nothing to match the title by.
    /// </summary>
    [Fact]
    public void RequestEvent_Data_CarriesJoinersAndIds()
    {
        var request = Dune();
        var joiners = new[] { Guid.NewGuid(), Guid.NewGuid() };
        request.Joined = joiners.Select(j => new RequestJoiner { UserId = j, UserName = "joiner" }).ToList();

        var data = RequestNotifier.RequestEvent(NotificationEvent.Approved, request, 0, Now).Data;

        Assert.Equal("Approved", data["event"]);
        Assert.Equal(request.Id.ToString("N"), data["requestId"]);
        Assert.Equal("Approved", data["status"]);
        Assert.Equal(string.Join(',', joiners.Select(j => j.ToString("N"))), data["joinerIds"]);
        Assert.Equal("438631", data["tmdbId"]);
        Assert.Equal("2", data["joined"]);
        Assert.Equal("Dune (2021)", data["title"]);
    }

    /// <summary>
    /// A failed request's reason is that it is tried again, never Radarr's or Sonarr's own error.
    /// True: a requester never reads a server's error or its name.
    /// False: a server's error, which may name it or its address, would reach requesters.
    /// </summary>
    [Fact]
    public void RequestEvent_Failed_HidesTheServersError()
    {
        var request = Dune();
        request.Note = "Radarr at 10.0.0.5 refused the API key.";

        var made = RequestNotifier.RequestEvent(NotificationEvent.Failed, request, 0, Now);

        Assert.Equal(RequestEvents.Failed, made.Key);
        Assert.DoesNotContain("10.0.0.5", made.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("10.0.0.5", string.Join(' ', made.Data.Values), StringComparison.Ordinal);
        Assert.Equal("It will be tried again.", made.Data["reason"]);
    }

    /// <summary>
    /// A request with no requester is about no one.
    /// True: an event for a request without a user is sent to the groups alone.
    /// False: an event would name an empty user id.
    /// </summary>
    [Fact]
    public void RequestEvent_NoRequester_IsAboutNoOne()
    {
        var request = Dune();
        request.UserId = Guid.Empty;
        request.UserName = string.Empty;

        var made = RequestNotifier.RequestEvent(NotificationEvent.Created, request, 0, Now);

        Assert.Null(made.UserId);
        Assert.Equal("Someone requested Dune (2021).", made.Body);
    }

    /// <summary>
    /// Days say how long a request waited, or how long is left, and the reminders' wording fills them in.
    /// True: an aging reminder says 5 days ago and an expiring one rejected in 1 day.
    /// False: a reminder would count the wrong days, or show {days}.
    /// </summary>
    [Fact]
    public void Values_Days_SayHowLongItWaitedOrIsLeft()
    {
        var request = Dune();
        request.CreatedUtc = Now.AddDays(-5);
        request.AgingNotifiedUtc = Now;
        request.ExpiringNotifiedDays = 1;
        request.Status = RequestStatus.Pending;

        var aging = RequestNotifier.RequestEvent(NotificationEvent.Aging, request, 10, Now);
        var expiring = RequestNotifier.RequestEvent(NotificationEvent.Expiring, request, 10, Now);

        Assert.Equal("5 days", RequestNotifier.Values(NotificationEvent.Aging, request, 10, Now)["days"]);
        Assert.Equal("1 day", RequestNotifier.Values(NotificationEvent.Expiring, request, 10, Now)["days"]);
        Assert.Equal("Waiting on approval: Dune (2021)", aging.Title);
        Assert.Contains("5 days ago", aging.Body, StringComparison.Ordinal);
        Assert.Equal("Dune (2021) is rejected in 1 day unless approved.", expiring.Body);
        Assert.Equal(RequestEvents.Expiring, expiring.Key);
    }

    /// <summary>
    /// The values count who joined, name the seasons of a show, and leave an empty note empty.
    /// True: the wording can say how many joined and which seasons were asked for.
    /// False: the count or the seasons would be missing from what a webhook carries.
    /// </summary>
    [Fact]
    public void Values_Request_CountJoinersAndNameSeasons()
    {
        var show = new MediaRequest { Type = RequestType.Series, Title = "Severance", Year = 2022, Seasons = new List<int> { 2 }, UserName = "viewer" };
        var movie = Dune();
        movie.Joined = new List<RequestJoiner> { new() { UserName = "Dana" }, new() { UserName = "Sam" } };

        var shown = RequestNotifier.Values(NotificationEvent.Created, show, 0, Now);

        Assert.Equal("2", RequestNotifier.Values(NotificationEvent.Created, movie, 0, Now)["joined"]);
        Assert.Equal("0", shown["joined"]);
        Assert.Equal("Season 2", shown["seasons"]);
        Assert.Equal(string.Empty, RequestNotifier.Values(NotificationEvent.Created, movie, 0, Now)["seasons"]);
        Assert.Equal(string.Empty, shown["note"]);
    }

    /// <summary>
    /// Fill fills known tokens, leaves unknown ones out, and tidies the spaces an empty value leaves.
    /// True: a template reads as a sentence whatever values are empty.
    /// False: an event would show {unknown} or double spaces.
    /// </summary>
    [Fact]
    public void Fill_Tokens_FillsKnownAndDropsUnknown()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["title"] = "*Dune* (2021)", ["user"] = "viewer", ["reason"] = string.Empty };

        Assert.Equal("Sorry viewer, no *Dune* (2021).", RequestNotifier.Fill("Sorry {user}, no {title}. {reason} {unknown}", values));
        Assert.Equal("No to *Dune* (2021) for viewer", RequestNotifier.Fill("No to {TITLE}  for   {user}", values));
        Assert.Equal(string.Empty, RequestNotifier.Fill(null!, values));
    }

    /// <summary>
    /// Days left are rounded up, and days waiting rounded down and never below 0.
    /// True: a reminder never says fewer days are left, or a negative wait.
    /// False: a reminder would say 2 days with 2.5 left.
    /// </summary>
    [Fact]
    public void DaysWaiting_AndDaysLeft_Round()
    {
        Assert.Equal(3, RequestNotifier.DaysLeft(Now.AddDays(-7.5), 10, Now));
        Assert.Equal(2, RequestNotifier.DaysWaiting(Now.AddDays(-2.9), Now));
        Assert.Equal(0, RequestNotifier.DaysWaiting(Now.AddDays(1), Now));
    }

    /// <summary>
    /// An issue's event has its key, its title and body, is about the first reporter, opens the issue page, and carries every reporter.
    /// True: Notifications sends Audio issue reported about the first reporter, with every reporter's id.
    /// False: an issue event would be about no one, or leave out who else reported.
    /// </summary>
    [Fact]
    public void IssueEvent_Reported_IsAboutTheFirstReporter()
    {
        var issue = Issue();

        var made = RequestNotifier.IssueEvent(NotificationEvent.IssueReported, issue);

        Assert.Equal(IssueEvents.Created, made.Key);
        Assert.Equal("Audio issue reported", made.Title);
        Assert.Equal("viewer reported The Lighthouse Keeper (2019). The sound drops out.", made.Body);
        Assert.Equal(issue.Reporters[0].UserId, made.UserId);
        Assert.Equal("issues/#/item?id=" + issue.ItemId.ToString("N"), made.Url);
        Assert.Equal(NectoEventLevel.Warning, made.Level);
        Assert.Equal(string.Join(',', issue.Reporters.Select(r => r.UserId.ToString("N"))), made.Data["reporterIds"]);
        Assert.Equal(issue.Id.ToString("N"), made.Data["issueId"]);
        Assert.Equal(issue.ItemId.ToString("N"), made.Data["itemId"]);
        Assert.Equal("1", made.Data["joined"]);
    }

    /// <summary>
    /// A decided issue's event says its state and the manager's last note.
    /// True: reporters read Issue declined and why.
    /// False: a decline would read as resolved, or lose its reason.
    /// </summary>
    [Fact]
    public void IssueEvent_Declined_SaysTheStateAndNote()
    {
        var issue = Issue();
        issue.State = IssueState.Declined;
        issue.Notes.Add(new IssueNote { UserId = Guid.NewGuid(), User = "admin", Text = "Plays fine here.", Admin = true, Time = Now });

        var made = RequestNotifier.IssueEvent(NotificationEvent.IssueResolved, issue);

        Assert.Equal(IssueEvents.Updated, made.Key);
        Assert.Equal(("Issue declined", "The issue with The Lighthouse Keeper (2019) was declined. Plays fine here."), (made.Title, made.Body));
        Assert.Equal(NectoEventLevel.Info, made.Level);
        Assert.Equal("declined", made.Data["state"]);
    }

    /// <summary>
    /// Notify sends a copy of the request in the background, taken when it was called.
    /// True: a change to the request after Notify cannot change what is sent.
    /// False: a later change would leak into the event, or the event would never be sent.
    /// </summary>
    [Fact]
    public void Notify_Request_SendsACopyTakenNow()
    {
        using var harness = new RequestHarness();
        var request = Dune();

        harness.Notifier.Notify(NotificationEvent.Created, request);
        request.Title = "Changed";

        var sent = Assert.Single(harness.Events.Sent);
        Assert.Equal(RequestEvents.Created, sent.Key);
        Assert.Contains("Dune", sent.Title, StringComparison.Ordinal);
    }

    /// <summary>
    /// A channel that fails is logged and never thrown back at the caller.
    /// True: a broken Notifications never fails a request.
    /// False: approving a request would fail with the notifier.
    /// </summary>
    [Fact]
    public void Notify_NotifierThrows_DoesNotThrow()
    {
        var (failing, stub) = InterfaceStub.Create<INotifier>();
        stub.Answer("SendAsync", _ => Task.FromException(new InvalidOperationException("down")));
        var notifier = new RequestNotifier(failing, NullLogger<RequestNotifier>.Instance);

        var error = Record.Exception(() =>
        {
            notifier.Notify(NotificationEvent.Created, Dune());
            notifier.NotifyIssue(NotificationEvent.IssueReported, Issue());
        });

        Assert.Null(error);
        Assert.Equal(2, stub.Calls.Count);
    }

    private static MediaRequest Dune() => new()
    {
        Id = Guid.NewGuid(),
        Type = RequestType.Movie,
        Title = "Dune",
        Year = 2021,
        UserId = Guid.NewGuid(),
        UserName = "viewer",
        Status = RequestStatus.Approved,
        ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [ProviderIds.Tmdb] = "438631" },
        CreatedUtc = Now,
        UpdatedUtc = Now
    };

    private static Issue Issue()
    {
        var first = Guid.NewGuid();
        return new Issue
        {
            Id = Guid.NewGuid(),
            ItemId = Guid.NewGuid(),
            ItemType = "Movie",
            Title = "The Lighthouse Keeper",
            Year = 2019,
            Reporters =
            {
                new IssueReporter { UserId = first, UserName = "viewer", Type = IssueType.Audio, ReportedUtc = Now },
                new IssueReporter { UserId = Guid.NewGuid(), UserName = "other", Type = IssueType.Video, ReportedUtc = Now }
            },
            Notes = { new IssueNote { UserId = first, User = "viewer", Text = "The sound drops out.", Time = Now } }
        };
    }
}
