using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Configuration.Notifications;
using Jellyfin.Plugin.Necto.Services.Issues.Models;
using Jellyfin.Plugin.Necto.Services.Notifications;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Siphonophore.Stolon;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Requests;

/// <summary>
/// Each request and issue change becomes Necto events: the channels' event worded for administrators, the requester's
/// and joiners' own, and an issue's one event for its reporters. Each has its key, its words in the server's language,
/// the people it is about and leaves out, the page it opens, its level, its poster, and what a post carries.
/// Sending reads the loaded plugin's settings, so it runs on its own.
/// </summary>
[Collection(ServicesCollection.Name)]
public class RequestNotifierTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

    private static readonly Guid AdminId = Guid.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

    /// <summary>
    /// A request's event has its key, its title and body filled, and is about the requester.
    /// True: Notifications sends Approved: Dune (2021) about the one who asked.
    /// False: an event would carry a template, the wrong key, or no one it is about.
    /// </summary>
    [Fact]
    public void RequestEvent_Approved_IsFilledAndAboutTheRequester()
    {
        var request = Dune();

        var made = RequestNotifier.RequestEvent(NotificationEvent.Approved, request, Plain());

        Assert.Equal(RequestEvents.Approved, made.Key);
        Assert.Equal("Approved: Dune (2021)", made.Title);
        Assert.Equal("Dune (2021) was approved.", made.Body);
        Assert.Equal(request.UserId, made.UserId);
        Assert.Equal(NectoEventLevel.Info, made.Level);
    }

    /// <summary>
    /// A request's event opens the title in the library when it is there, and the title on the request page when not.
    /// True: a notification about a new request opens that title, not the request page's home.
    /// False: a notification would open a page that does not show the title.
    /// </summary>
    [Fact]
    public void RequestEvent_Url_IsTheTitleInTheLibraryOrOnTheRequestPage()
    {
        var request = Dune();
        var inLibrary = Dune();
        inLibrary.LibraryItemId = Guid.Parse("22222222222222222222222222222222");

        Assert.Equal("requests/#/details?type=Movie&name=Dune&tmdb=438631", RequestNotifier.RequestEvent(NotificationEvent.Created, request, Plain()).Url);
        Assert.Equal("web/#/details?id=22222222222222222222222222222222", RequestNotifier.RequestEvent(NotificationEvent.Available, inLibrary, Plain()).Url);
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
        => Assert.Equal(level, RequestNotifier.RequestEvent(what, Dune(), Plain(10)).Level);

    /// <summary>
    /// A request's posted data carries the event, the request, its status, its type, and each provider id, and its
    /// placeholders carry the words and every joiner's id. Nothing that names a person is posted by default.
    /// True: a webhook matches the title by its ids, and only a custom body names who asked.
    /// False: every webhook would carry the requester's name and note.
    /// </summary>
    [Fact]
    public void RequestEvent_Data_CarriesIdsAndLeavesPeopleToPlaceholders()
    {
        var request = Dune();
        request.RequesterNote = "For movie night";
        var joiners = new[] { Guid.NewGuid(), Guid.NewGuid() };
        request.Joined = joiners.Select(j => new RequestJoiner { UserId = j, UserName = "joiner" }).ToList();

        var made = RequestNotifier.RequestEvent(NotificationEvent.Approved, request, Plain());

        Assert.Equal("Approved", made.Data["event"]);
        Assert.Equal(request.Id.ToString("N"), made.Data["requestId"]);
        Assert.Equal("Approved", made.Data["status"]);
        Assert.Equal("Movie", made.Data["type"]);
        Assert.Equal("438631", made.Data["tmdbId"]);
        Assert.False(made.Data.ContainsKey("user"));
        Assert.False(made.Data.ContainsKey("note"));
        Assert.False(made.Data.ContainsKey("joinerIds"));
        Assert.Equal(string.Join(',', joiners.Select(j => j.ToString("N"))), made.Values["joinerIds"]);
        Assert.Equal("2", made.Values["joined"]);
        Assert.Equal("viewer", made.Values["user"]);
        Assert.Equal("Dune (2021)", made.Values["title"]);
    }

    /// <summary>
    /// A refused request tells administrators and channels what Radarr or Sonarr said, and tells the requester only that
    /// it is tried again.
    /// True: administrators can fix the server, and a requester never reads its error.
    /// False: the error would reach requesters, or administrators would not know why it failed.
    /// </summary>
    [Fact]
    public void EventsFor_Failed_GivesTheErrorToChannelsAndNotTheRequester()
    {
        var request = Dune();
        request.Note = "Radarr at 10.0.0.5 refused the API key.";

        var events = RequestNotifier.EventsFor(NotificationEvent.Failed, request, Plain());

        var channels = events.Single(e => e.Audience == NectoEventAudience.Channels);
        var own = events.Single(e => e.Audience == NectoEventAudience.Users);
        Assert.Equal(RequestEvents.Failed, own.Key);
        Assert.Contains("10.0.0.5", channels.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("10.0.0.5", own.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("10.0.0.5", string.Join(' ', own.Values.Values.Concat(own.Data.Values)), StringComparison.Ordinal);
        Assert.Equal("It will be tried again.", own.Values["reason"]);
        Assert.Equal(request.UserId, own.UserId);
    }

    /// <summary>
    /// A request with no requester is about no one, and only its channels hear of it.
    /// True: an event for a request without a user names no empty id.
    /// False: an event would name an empty user id, or be sent to no one.
    /// </summary>
    [Fact]
    public void EventsFor_NoRequester_GoesToTheChannelsAlone()
    {
        var request = Dune();
        request.UserId = Guid.Empty;
        request.UserName = string.Empty;

        var events = RequestNotifier.EventsFor(NotificationEvent.Created, request, Plain());

        var made = Assert.Single(events);
        Assert.Null(made.UserId);
        Assert.Equal(NectoEventAudience.Channels, made.Audience);
        Assert.Equal("Someone requested Dune (2021).", made.Body);
    }

    /// <summary>
    /// An approval goes to the channels without the requester or the deciding administrator, to the requester, and to
    /// those who joined, split by whether they may see requesters.
    /// True: everyone who follows the request hears of it once, and the administrator is not told of their own choice.
    /// False: joiners would hear nothing, or the administrator would be told what they just did.
    /// </summary>
    [Fact]
    public void EventsFor_Approved_ReachesRequesterAndJoinersAndLeavesTheDeciderOut()
    {
        var request = Dune();
        request.DecidedBy = "admin";
        request.RequesterNote = "For movie night";
        var sees = Guid.NewGuid();
        var hidden = Guid.NewGuid();
        request.Joined = new List<RequestJoiner> { new() { UserId = sees, UserName = "manager" }, new() { UserId = hidden, UserName = "guest" } };
        var context = new NoticeContext(0, Now, string.Empty, name => name == "admin" ? AdminId : null, id => id == sees);

        var events = RequestNotifier.EventsFor(NotificationEvent.Approved, request, context);

        Assert.Equal(4, events.Count);
        var channels = events[0];
        Assert.Equal(NectoEventAudience.Channels, channels.Audience);
        Assert.Equal(new[] { request.UserId, AdminId }, channels.ExceptUserIds);
        var own = events[1];
        Assert.Equal((NectoEventAudience.Users, (Guid?)request.UserId), (own.Audience, own.UserId));
        Assert.Equal("For movie night", own.Values["note"]);
        var seeing = events.Single(e => e.OtherUserIds.Contains(sees));
        var notSeeing = events.Single(e => e.OtherUserIds.Contains(hidden));
        Assert.Null(seeing.UserId);
        Assert.Equal(NectoEventAudience.Users, seeing.Audience);
        Assert.Equal("viewer", seeing.Values["user"]);
        Assert.Equal(string.Empty, seeing.Values["note"]);
        Assert.Equal("Someone", notSeeing.Values["user"]);
        Assert.Equal(string.Empty, notSeeing.Values["note"]);
    }

    /// <summary>
    /// A requester who approved their own request is not told, but those who joined still are.
    /// True: an auto approved user gets no Approved right after asking.
    /// False: every self approved request would ping its requester.
    /// </summary>
    [Fact]
    public void EventsFor_SelfApproved_SkipsTheRequesterAndKeepsJoiners()
    {
        var request = Dune();
        request.DecidedBy = "viewer";
        var joiner = Guid.NewGuid();

        var alone = RequestNotifier.EventsFor(NotificationEvent.Approved, request, Plain());
        request.Joined = new List<RequestJoiner> { new() { UserId = joiner, UserName = "guest" } };
        var joined = RequestNotifier.EventsFor(NotificationEvent.Approved, request, Plain());

        Assert.Equal(NectoEventAudience.Channels, Assert.Single(alone).Audience);
        Assert.Equal(2, joined.Count);
        Assert.DoesNotContain(joined, e => e.UserId == request.UserId && e.Audience == NectoEventAudience.Users);
        Assert.Contains(joined, e => e.OtherUserIds.Contains(joiner));
    }

    /// <summary>
    /// Reminders go to the channels and to the groups that pick them, never to the requester. An aging one shows on one
    /// app, and an expiring one is not held for an administrator who is away.
    /// True: one nudge per administrator, and no pile of out of date day counts.
    /// False: a reminder would show on every device, or old counts would arrive later.
    /// </summary>
    [Fact]
    public void EventsFor_Reminders_FollowTheirDeliveryRules()
    {
        var request = Dune();
        request.Status = RequestStatus.Pending;
        request.Joined = new List<RequestJoiner> { new() { UserId = Guid.NewGuid(), UserName = "guest" } };

        var aging = Assert.Single(RequestNotifier.EventsFor(NotificationEvent.Aging, request, Plain(10)));
        var expiring = Assert.Single(RequestNotifier.EventsFor(NotificationEvent.Expiring, request, Plain(10)));

        Assert.Equal(NectoEventAudience.All, aging.Audience);
        Assert.True(aging.OneDevice);
        Assert.True(aging.Hold);
        Assert.False(expiring.OneDevice);
        Assert.False(expiring.Hold);
        Assert.Contains(request.UserId, aging.ExceptUserIds);
        Assert.Empty(aging.OtherUserIds);
    }

    /// <summary>
    /// The channels' event carries a version that names no one, for a post that leaves the requester out.
    /// True: a Discord channel with Requester off says Someone requested Dune.
    /// False: every post would name who asked.
    /// </summary>
    [Fact]
    public void EventsFor_Anonymous_NamesNoOne()
    {
        var request = Dune();

        var channels = RequestNotifier.EventsFor(NotificationEvent.Created, request, Plain())[0];

        var anonymous = Assert.IsType<NectoEvent>(channels.Anonymous);
        Assert.Equal("Someone requested Dune (2021).", anonymous.Body);
        Assert.Null(anonymous.UserId);
        Assert.DoesNotContain("viewer", anonymous.Fields.Single(f => f.Key == PostFields.User).Value, StringComparison.Ordinal);
        Assert.DoesNotContain("viewer", anonymous.Fields.Single(f => f.Key == PostFields.Request).Value, StringComparison.Ordinal);
    }

    /// <summary>
    /// A request's post fields carry its summary, requester, joined count, seasons, note, and the whole request as JSON.
    /// True: a Discord embed and a webhook carry what the old Requests posts did.
    /// False: posts would carry only the title.
    /// </summary>
    [Fact]
    public void RequestEvent_Fields_CarryWhatAPostShows()
    {
        var show = Dune();
        show.Type = RequestType.Series;
        show.Seasons = new List<int> { 1, 2 };
        show.Overview = "Spice.";
        show.RequesterNote = "Please";

        var fields = RequestNotifier.RequestEvent(NotificationEvent.Created, show, Plain()).Fields.ToDictionary(f => f.Key);

        Assert.True(fields[PostFields.Overview].InText);
        Assert.Equal("viewer", fields[PostFields.User].Value);
        Assert.Equal(string.Empty, fields[PostFields.Joined].Value);
        Assert.Contains("1", fields[PostFields.Seasons].Value, StringComparison.Ordinal);
        Assert.Equal("Please", fields[PostFields.Note].Value);
        Assert.True(fields[PostFields.Request].Json);
        Assert.Equal("Series", JsonDocument.Parse(fields[PostFields.Request].Value).RootElement.GetProperty("Type").GetString());
    }

    /// <summary>
    /// A poster is made absolute with the server's address when stored relative, kept when absolute, and dropped otherwise.
    /// True: Discord and browsers get a poster they can load.
    /// False: a relative poster would fail the Discord post, or a script address would be sent.
    /// </summary>
    [Theory]
    [InlineData("../Items/abc/Images/Primary", "https://media.example.com", "https://media.example.com/Items/abc/Images/Primary")]
    [InlineData("../Items/abc/Images/Primary", "", null)]
    [InlineData("https://image.tmdb.org/t/p/w500/a.jpg", "", "https://image.tmdb.org/t/p/w500/a.jpg")]
    [InlineData("javascript:alert(1)", "https://media.example.com", null)]
    [InlineData(null, "https://media.example.com", null)]
    public void PosterUrl_IsAbsoluteOrNothing(string? poster, string server, string? expected)
        => Assert.Equal(expected, RequestNotifier.PosterUrl(poster, server));

    /// <summary>
    /// A request's events carry its poster and one tag and subject for the request.
    /// True: a newer update about the same request replaces the older one, and other requests' stay.
    /// False: every new request would replace the last one's notification.
    /// </summary>
    [Fact]
    public void RequestEvent_PosterAndTag_BelongToTheRequest()
    {
        var request = Dune();
        request.PosterUrl = "../Items/abc/Images/Primary";

        var made = RequestNotifier.RequestEvent(NotificationEvent.Created, request, Plain(0, "https://media.example.com"));

        Assert.Equal("https://media.example.com/Items/abc/Images/Primary", made.ImageUrl);
        Assert.Equal("requests-" + request.Id.ToString("N"), made.Tag);
        Assert.Equal(made.Tag, made.Subject);
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

        var aging = RequestNotifier.RequestEvent(NotificationEvent.Aging, request, Plain(10));
        var expiring = RequestNotifier.RequestEvent(NotificationEvent.Expiring, request, Plain(10));

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
    /// An issue's event has its key, its title and body, is about the first reporter, reaches every other reporter,
    /// leaves the reporters out of the administrators, and opens the issue page.
    /// True: every reporter hears of the issue, and an administrator who reported it hears once.
    /// False: an issue event would reach only the first reporter.
    /// </summary>
    [Fact]
    public void IssueEvent_Reported_ReachesEveryReporter()
    {
        var issue = Issue();

        var made = RequestNotifier.IssueEvent(NotificationEvent.IssueReported, issue, Plain());

        Assert.Equal(IssueEvents.Created, made.Key);
        Assert.Equal("Audio issue reported", made.Title);
        Assert.Equal("viewer reported The Lighthouse Keeper (2019). The sound drops out.", made.Body);
        Assert.Equal(issue.Reporters[0].UserId, made.UserId);
        Assert.Equal(new[] { issue.Reporters[1].UserId }, made.OtherUserIds);
        Assert.Equal(issue.Reporters.Select(r => r.UserId), made.ExceptUserIds);
        Assert.Equal("issues/#/item?id=" + issue.ItemId.ToString("N"), made.Url);
        Assert.Equal(NectoEventLevel.Warning, made.Level);
        Assert.Equal(string.Join(',', issue.Reporters.Select(r => r.UserId.ToString("N"))), made.Values["reporterIds"]);
        Assert.Equal(issue.Id.ToString("N"), made.Data["issueId"]);
        Assert.Equal(issue.ItemId.ToString("N"), made.Data["itemId"]);
        Assert.Equal("1", made.Values["joined"]);
        Assert.Equal("issues-" + issue.Id.ToString("N"), made.Tag);
    }

    /// <summary>
    /// A decided issue's event says its state and the manager's last note, and leaves out the administrator who decided.
    /// True: reporters read Issue declined and why, and the deciding administrator is not told of their own choice.
    /// False: a decline would read as resolved, lose its reason, or reach the one who made it.
    /// </summary>
    [Fact]
    public void IssueEvent_Declined_SaysTheStateAndLeavesTheDeciderOut()
    {
        var issue = Issue();
        issue.State = IssueState.Declined;
        issue.DecidedBy = "admin";
        issue.Notes.Add(new IssueNote { UserId = AdminId, User = "admin", Text = "Plays fine here.", Admin = true, Time = Now });
        var context = new NoticeContext(0, Now, string.Empty, name => name == "admin" ? AdminId : null, _ => false);

        var made = RequestNotifier.IssueEvent(NotificationEvent.IssueResolved, issue, context);

        Assert.Equal(IssueEvents.Updated, made.Key);
        Assert.Equal(("Issue declined", "The issue with The Lighthouse Keeper (2019) was declined. Plays fine here."), (made.Title, made.Body));
        Assert.Equal(NectoEventLevel.Info, made.Level);
        Assert.Equal("declined", made.Values["state"]);
        Assert.Equal(new[] { AdminId }, made.ExceptUserIds);
    }

    /// <summary>
    /// An issue's poster is its item's, or its series', from the server's address, and its post names no reporter
    /// when the channel leaves them out.
    /// True: Discord shows the poster, and an anonymous post says Someone reported it.
    /// False: issue posts would carry no poster, or always name the reporter.
    /// </summary>
    [Fact]
    public void IssueEvent_PosterAndAnonymous()
    {
        var issue = Issue();
        issue.SeriesId = Guid.Parse("33333333333333333333333333333333");

        var made = RequestNotifier.IssueEvent(NotificationEvent.IssueReported, issue, Plain(0, "https://media.example.com/"));

        Assert.Equal("https://media.example.com/Items/33333333333333333333333333333333/Images/Primary?fillHeight=480&quality=90", made.ImageUrl);
        var anonymous = Assert.IsType<NectoEvent>(made.Anonymous);
        Assert.StartsWith("Someone reported", anonymous.Body, StringComparison.Ordinal);
        Assert.Null(anonymous.UserId);
        Assert.Null(RequestNotifier.IssuePosterUrl(issue, string.Empty));
    }

    /// <summary>
    /// Notify sends copies of the request in the background, taken when it was called: the channels' and the requester's.
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

        Assert.Equal(2, harness.Events.Sent.Count);
        Assert.All(harness.Events.Sent, sent =>
        {
            Assert.Equal(RequestEvents.Created, sent.Key);
            Assert.Contains("Dune", sent.Title, StringComparison.Ordinal);
        });
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
        Assert.Equal(3, stub.Calls.Count);
    }

    /// <summary>
    /// Forget drops the Jellyfin messages held about a removed request, and keeps the rest.
    /// True: a user coming back online is not told Approved: Dune once Dune's request is gone.
    /// False: a removed request's messages would still arrive.
    /// </summary>
    [Fact]
    public void Forget_DropsTheRemovedRequestsHeldMessages()
    {
        var path = Path.Combine(Path.GetTempPath(), "necto-held-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var held = new JsonNotifyHeldStore(path, time: TimeProvider.System);
            var request = Guid.NewGuid();
            var user = Guid.NewGuid();
            held.Hold(new NotifyHeldMessage(Guid.NewGuid(), user, "Approved", "Dune", DateTime.UtcNow, false, RequestNotifier.RequestSubject(request)));
            held.Hold(new NotifyHeldMessage(Guid.NewGuid(), user, "Approved", "Other", DateTime.UtcNow, false, RequestNotifier.RequestSubject(Guid.NewGuid())));
            var notifier = new RequestNotifier(InterfaceStub.Create<INotifier>().Proxy, NullLogger<RequestNotifier>.Instance, held: held);

            notifier.Forget(request);

            Assert.Equal("Other", Assert.Single(held.Take(user)).Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static NoticeContext Plain(int days = 0, string server = "") => NoticeContext.Plain(days, Now, server);

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
