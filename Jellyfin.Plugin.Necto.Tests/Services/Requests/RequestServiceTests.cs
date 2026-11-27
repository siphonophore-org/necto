using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Services.Notifications;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using MediaBrowser.Controller.Entities.Movies;
using Siphonophore.Stolon;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Requests;

/// <summary>
/// The request service holds each request to its requester's limits and the rules in Settings, sends approved ones to
/// Radarr, follows them into the library, and works out what each user may do from their groups. It reads the loaded
/// plugin's settings, so it runs on its own.
/// </summary>
[Collection(ServicesCollection.Name)]
public sealed class RequestServiceTests : IDisposable
{
    private static readonly TimeSpan SendWait = TimeSpan.FromSeconds(10);
    private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Dictionary<string, string> MatrixIds = new(StringComparer.OrdinalIgnoreCase) { ["Tmdb"] = "603" };
    private static readonly Dictionary<string, string> SimpsonsIds = new(StringComparer.OrdinalIgnoreCase) { [ProviderIds.Tmdb] = "456" };
    private static readonly TitleFacts Unknown = new(new List<string>(), null, null, false);

    private RequestHarness? _harness;

    private RequestHarness H => _harness ??= new RequestHarness();

    public void Dispose() => _harness?.Dispose();

    // ------------------------------------------------------------------ seasons and availability

    /// <summary>
    /// Two season choices overlap when either is the whole show or they share a season.
    /// True: a request for seasons 2 and 3 repeats one for 1 and 2.
    /// False: the same season could be requested twice, or unrelated seasons refused.
    /// </summary>
    [Theory]
    [InlineData(null, null, true)]
    [InlineData(new[] { 1 }, null, true)]
    [InlineData(new[] { 1, 2 }, new[] { 2, 3 }, true)]
    [InlineData(new[] { 1, 2 }, new[] { 3 }, false)]
    public void SeasonsOverlap_Choices_OverlapWhenTheyShare(int[]? a, int[]? b, bool expected)
        => Assert.Equal(expected, RequestService.SeasonsOverlap(a is null ? null : new List<int>(a), b is null ? null : new List<int>(b)));

    /// <summary>
    /// Seasons are sorted and kept once, and a choice with specials, a negative, or an absurd season is no choice.
    /// True: a request stores the seasons asked for in order.
    /// False: specials could be picked as a season, or a bad number would reach Sonarr.
    /// </summary>
    [Fact]
    public void NormalizeSeasons_Choice_IsSortedOrInvalid()
    {
        Assert.Null(RequestService.NormalizeSeasons(null));
        Assert.Null(RequestService.NormalizeSeasons(new List<int>()));
        Assert.Null(RequestService.NormalizeSeasons(new List<int> { -1 }));
        Assert.Null(RequestService.NormalizeSeasons(new List<int> { 0 }));
        Assert.Null(RequestService.NormalizeSeasons(new List<int> { 0, 1, 2 }));
        Assert.Null(RequestService.NormalizeSeasons(new List<int> { 1, 501 }));
        Assert.Equal(new List<int> { 1, 2, 5 }, RequestService.NormalizeSeasons(new List<int> { 5, 1, 2, 2 }));
    }

    /// <summary>
    /// Seasons read as runs.
    /// True: a request shows Seasons 1-3, 5 rather than every number.
    /// False: a long run of seasons would read as a long list.
    /// </summary>
    [Fact]
    public void DescribeSeasons_Seasons_ReadAsRuns()
    {
        Assert.Equal("All seasons", RequestService.DescribeSeasons(null));
        Assert.Equal("Season 4", RequestService.DescribeSeasons(new List<int> { 4 }));
        Assert.Equal("Seasons 1-3, 5", RequestService.DescribeSeasons(new List<int> { 5, 2, 1, 3 }));
    }

    /// <summary>
    /// A show request is partly available with some of its seasons and available with all.
    /// True: a request shows which of its seasons arrived.
    /// False: a show would read as available with seasons still missing.
    /// </summary>
    [Fact]
    public void Availability_Show_PartlyThenWhollyAvailable()
    {
        var request = new MediaRequest { Type = RequestType.Series, Status = RequestStatus.Approved, Seasons = new List<int> { 1, 2 } };

        var partial = RequestService.Availability(request, RequestType.Series, Counts(new() { [1] = 8, [3] = 8 }));

        Assert.Equal((RequestStatus.Approved, (List<int>?)null), RequestService.Availability(request, RequestType.Series, Counts(new() { [3] = 8 })));
        Assert.Equal(RequestStatus.PartiallyAvailable, partial.Status);
        Assert.Equal(new List<int> { 1 }, partial.Have);
        Assert.Equal(RequestStatus.Available, RequestService.Availability(request, RequestType.Series, Counts(new() { [1] = 8, [2] = 8, [3] = 8 })).Status);
    }

    /// <summary>
    /// A season counts only once complete, by Sonarr's count or the guide's missing episodes.
    /// True: one imported episode of ten does not make the season available.
    /// False: the requester would hear a season arrived when one episode had.
    /// </summary>
    [Fact]
    public void Availability_Season_CountsOnlyOnceComplete()
    {
        var request = new MediaRequest { Type = RequestType.Series, Status = RequestStatus.Approved, Seasons = new List<int> { 1 }, ExpectedEpisodes = new() { [1] = 10 } };
        var plain = new MediaRequest { Type = RequestType.Series, Status = RequestStatus.Approved, Seasons = new List<int> { 1 } };

        Assert.Equal(RequestStatus.Approved, RequestService.Availability(request, RequestType.Series, Counts(new() { [1] = 1 })).Status);
        Assert.Equal(RequestStatus.Available, RequestService.Availability(request, RequestType.Series, Counts(new() { [1] = 10 })).Status);
        Assert.Equal(RequestStatus.Approved, RequestService.Availability(plain, RequestType.Series, Counts(new() { [1] = 3 }, 1)).Status);
    }

    /// <summary>
    /// A whole show is available only once every season is, or without Sonarr's list, once all it holds is complete.
    /// True: a whole show request waits for its last season.
    /// False: the first season would make the whole show available.
    /// </summary>
    [Fact]
    public void Availability_WholeShow_NeedsEverySeason()
    {
        var whole = new MediaRequest { Type = RequestType.Series, Status = RequestStatus.Approved, ExpectedEpisodes = new Dictionary<int, int> { [1] = 8, [2] = 8, [3] = 8 } };
        var unknown = new MediaRequest { Type = RequestType.Series, Status = RequestStatus.Approved };

        var first = RequestService.Availability(whole, RequestType.Series, Counts(new() { [1] = 8 }));

        Assert.Equal(RequestStatus.Approved, RequestService.Availability(whole, RequestType.Series, new SeasonCounts()).Status);
        Assert.Equal(RequestStatus.PartiallyAvailable, first.Status);
        Assert.Equal(new List<int> { 1 }, first.Have);
        Assert.Equal(RequestStatus.Available, RequestService.Availability(whole, RequestType.Series, Counts(new() { [1] = 8, [2] = 8, [3] = 8 })).Status);
        Assert.Equal(RequestStatus.Available, RequestService.Availability(unknown, RequestType.Series, Counts(new() { [1] = 8 })).Status);
        Assert.Equal(RequestStatus.PartiallyAvailable, RequestService.Availability(unknown, RequestType.Series, Counts(new() { [1] = 8, [2] = 2 }, 2)).Status);
    }

    /// <summary>
    /// A request still waiting keeps its state when partly there.
    /// True: a partly arrived request stays in the approve or retry queue.
    /// False: it would drop out of the queue it still needs.
    /// </summary>
    [Theory]
    [InlineData(RequestStatus.Pending)]
    [InlineData(RequestStatus.Failed)]
    public void Availability_StillWaiting_KeepsItsState(RequestStatus waiting)
    {
        var request = new MediaRequest { Type = RequestType.Series, Status = waiting, Seasons = new List<int> { 1, 2, 3 } };

        var result = RequestService.Availability(request, RequestType.Series, Counts(new() { [1] = 8 }));

        Assert.Equal(waiting, result.Status);
        Assert.Equal(new List<int> { 1 }, result.Have);
    }

    /// <summary>
    /// An open request blocks only the seasons it still waits for.
    /// True: season 1 can be requested again once it has left a partly available request.
    /// False: an arrived season would stay blocked.
    /// </summary>
    [Fact]
    public void OutstandingSeasons_PartlyArrived_AreTheRest()
    {
        var partly = new MediaRequest { Seasons = new List<int> { 1, 2 }, AvailableSeasons = new List<int> { 1 } };

        Assert.Equal(new List<int> { 2 }, RequestService.OutstandingSeasons(partly));
        Assert.False(RequestService.SeasonsOverlap(RequestService.OutstandingSeasons(partly), new List<int> { 1 }));
        Assert.Null(RequestService.OutstandingSeasons(new MediaRequest()));
    }

    /// <summary>
    /// A quota frees up as requests age out of its window, and says when.
    /// True: a requester at their limit is told when the next request is free.
    /// False: the quota would never free up, or say the wrong time.
    /// </summary>
    [Fact]
    public void QuotaFrom_UsedUp_FreesAsRequestsAgeOut()
    {
        var day = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var quota = RequestService.QuotaFrom(new List<(DateTime, int)> { (day, 1), (day.AddDays(1), 1), (day.AddDays(2), 1) }, 2, 7)!;
        var spare = RequestService.QuotaFrom(new List<(DateTime, int)> { (day, 1) }, 2, 7)!;

        Assert.Null(RequestService.QuotaFrom(new List<(DateTime, int)>(), 0, 7));
        Assert.Equal(0, quota.Remaining);
        Assert.Equal(day.AddDays(8), quota.NextFreeUtc);
        Assert.Contains("frees up in", RequestService.QuotaMessage(RequestType.Movie, quota), StringComparison.Ordinal);
        Assert.Equal(1, spare.Remaining);
        Assert.Null(spare.NextFreeUtc);
    }

    /// <summary>
    /// A used up quota says there is room again once the first request ages out.
    /// True: the requester reads how many shows they used and over how long.
    /// False: the message would miscount or give no time.
    /// </summary>
    [Fact]
    public void QuotaMessage_UsedUp_SaysWhenThereIsRoom()
    {
        var day = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var quota = RequestService.QuotaFrom(new List<(DateTime, int)> { (day, 1), (day.AddDays(1), 1), (day.AddDays(2), 1) }, 3, 7)!;

        Assert.Equal(0, quota.Remaining);
        Assert.Equal(day.AddDays(7), quota.NextFreeUtc);
        Assert.StartsWith("You have used your 3 show requests for the last 7 days.", RequestService.QuotaMessage(RequestType.Series, quota), StringComparison.Ordinal);
    }

    /// <summary>
    /// A refusal is said in the caller's language, falling back to US English.
    /// True: a German page reads the quota in German, or English where no German string exists.
    /// False: a refusal would show a key, or another language than the caller's.
    /// </summary>
    [Fact]
    public void QuotaMessage_Language_IsTheCallersOrUsEnglish()
    {
        Assert.Equal("You have used your 1 movie request for the last 7 days.", RequestService.QuotaMessage(RequestType.Movie, new QuotaInfo { Limit = 1, Days = 7 }));
        Assert.Equal("You have used your 3 show requests for the last 7 days.", RequestService.QuotaMessage(RequestType.Series, new QuotaInfo { Limit = 3, Days = 7 }, "de"));
        Assert.Equal("Abbrechen", StolonTranslations.Get(TranslationKeys.ButtonCancel, "de"));
    }

    /// <summary>
    /// Only https posters from the artwork hosts, and the plugin's own library poster paths, are kept.
    /// True: a request's poster is one the page's policy allows and cannot break out of its style.
    /// False: a crafted poster address could load from anywhere or inject styles.
    /// </summary>
    [Theory]
    [InlineData("https://image.tmdb.org/t/p/original/a.jpg", "https://image.tmdb.org/t/p/w342/a.jpg")]
    [InlineData("https://artworks.thetvdb.com/banners/p.jpg", "https://artworks.thetvdb.com/banners/p.jpg")]
    [InlineData("https://example.com/p.jpg", null)]
    [InlineData("https://image.tmdb.org/t/p/w342/a.jpg\f);position:fixed", null)]
    [InlineData("https://image.tmdb.org:8443/t/p/w342/a.jpg", null)]
    [InlineData("http://image.tmdb.org/t/p/original/a.jpg", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData(null, null)]
    [InlineData("../Items/0123456789abcdef0123456789abcdef/Images/Primary?fillHeight=480&quality=90", "../Items/0123456789abcdef0123456789abcdef/Images/Primary?fillHeight=480&quality=90")]
    [InlineData("../Items/0123456789abcdef0123456789abcdef/Images/Primary?fillHeight=480&quality=90&x=1", null)]
    [InlineData("../../etc/passwd", null)]
    public void SafePosterUrl_Addresses_KeepsOnlySafeOnes(string? input, string? expected)
        => Assert.Equal(expected, RequestService.SafePosterUrl(input));

    // ------------------------------------------------------------------ dropped by Radarr

    /// <summary>
    /// A request removed from Radarr stops holding a place until Radarr has it again.
    /// True: a title an administrator deleted in Radarr frees the requester's place, and returns it when added back.
    /// False: a dropped request would hold a place for nothing.
    /// </summary>
    [Fact]
    public void ApplyStatus_RemovedFromRadarr_StopsHoldingAPlace()
    {
        var request = Sent(Guid.NewGuid());
        Assert.True(request.HoldsAPlace);

        RequestService.ApplyStatus(request, new ArrStatus(null, null, "No longer in Radarr", Missing: true) { ServerId = "radarr" });
        Assert.True(request.RemovedFromArr);
        Assert.Equal(12, request.ArrId);
        Assert.Equal(RequestStatus.Approved, request.Status);
        Assert.True(request.IsOpen);
        Assert.False(request.HoldsAPlace);

        RequestService.ApplyStatus(request, new ArrStatus(30, null, "Searching") { ServerId = "radarr" });
        Assert.False(request.RemovedFromArr);
        Assert.Equal(30, request.ArrId);
        Assert.True(request.HoldsAPlace);
    }

    /// <summary>
    /// A request never sent is not marked removed.
    /// True: a request still to be sent keeps its place.
    /// False: an unsent request would read as dropped.
    /// </summary>
    [Fact]
    public void ApplyStatus_NeverSent_IsNotMarkedRemoved()
    {
        var request = Sent(Guid.NewGuid());
        request.ArrId = null;

        RequestService.ApplyStatus(request, new ArrStatus(null, null, "Not in Radarr yet", Missing: true) { ServerId = "radarr" });

        Assert.False(request.RemovedFromArr);
        Assert.True(request.HoldsAPlace);
    }

    /// <summary>
    /// A dropped request blocks no one and its requester may remove it, while one still in Radarr is the managers' to remove.
    /// True: a dropped title can be requested again and cleared by its requester.
    /// False: a dropped request would block the title, or a requester could pull one from Radarr.
    /// </summary>
    [Fact]
    public void MayRemove_DroppedRequest_IsTheRequesters()
    {
        var mine = Guid.NewGuid();
        var dropped = Sent(mine);
        dropped.RemovedFromArr = true;
        var active = Sent(Guid.NewGuid());
        var requester = new UserAccess { UserId = mine };

        Assert.Null(RequestService.Duplicate(new[] { dropped }, RequestType.Movie, MatrixIds, null));
        Assert.Same(active, RequestService.Duplicate(new[] { dropped, active }, RequestType.Movie, MatrixIds, null));
        Assert.True(RequestService.MayRemove(dropped, requester));
        Assert.False(RequestService.MayRemove(dropped, new UserAccess { UserId = Guid.NewGuid() }));
        Assert.False(RequestService.MayRemove(Sent(mine), requester));
        Assert.True(RequestService.MayRemove(Sent(mine), new UserAccess { IsAdministrator = true }));

        dropped.Status = RequestStatus.Available;
        Assert.False(RequestService.MayRemove(dropped, requester));
    }

    // ------------------------------------------------------------------ rules

    /// <summary>
    /// Rules that need a title's details refuse when they could not be read, and decide from them when they could.
    /// True: a blocked genre or an unreleased rule cannot be dodged by a lookup failing, and managers are not held to them.
    /// False: a title would get past a rule because TMDb did not answer.
    /// </summary>
    [Fact]
    public void RuleRefusal_DetailsUnread_Refuses()
    {
        var genres = new RequestSettings { BlockedGenres = { "Horror" } };
        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Tmdb"] = "1" };

        Assert.Equal(RequestService.CouldNotCheck, RequestService.RuleRefusal(genres, Requester(), RequestType.Movie, ids, "Title", Unknown));
        Assert.Equal(RequestService.CouldNotCheck, RequestService.RuleRefusal(new RequestSettings { BlockedRatings = { "R" } }, Requester(), RequestType.Movie, ids, "Title", Unknown));
        Assert.Equal(RequestService.CouldNotCheck, RequestService.RuleRefusal(new RequestSettings(), Requester(unreleased: false), RequestType.Movie, ids, "Title", Unknown));
        Assert.Null(RequestService.RuleRefusal(new RequestSettings(), Requester(), RequestType.Movie, ids, "Title", Unknown));
        Assert.Null(RequestService.RuleRefusal(genres, Requester(), RequestType.Movie, ids, "Title", new TitleFacts(new List<string> { "Drama" }, "PG", true, true)));
        Assert.Null(RequestService.RuleRefusal(genres, new UserAccess { IsAdministrator = true }, RequestType.Movie, ids, "Title", Unknown));
        Assert.Equal(TranslationKeys.RequestsErrorTitleNotRequestable, RequestService.RuleRefusal(genres, Requester(), RequestType.Movie, ids, "Title", new TitleFacts(new List<string> { "horror" }, null, true, true)));
        Assert.Equal(TranslationKeys.RequestsErrorNotOutYet, RequestService.RuleRefusal(new RequestSettings(), Requester(unreleased: false), RequestType.Movie, ids, "Title", new TitleFacts(new List<string>(), null, false, true)));
        Assert.Equal(TranslationKeys.RequestsErrorNotOutYet, RequestService.RuleRefusal(new RequestSettings(), Requester(unreleased: false), RequestType.Movie, ids, "Title", new TitleFacts(new List<string>(), null, null, true)));
    }

    /// <summary>
    /// Blocked words are checked against the name TMDb knows, not only the one sent.
    /// True: a request for a blocked title under a harmless name is refused.
    /// False: the blocklist could be dodged by renaming the request.
    /// </summary>
    [Fact]
    public void RuleRefusal_BlockedWord_IsCheckedAgainstTmdbsName()
    {
        var config = new RequestSettings { BlockedKeywords = { "xxx" } };
        var facts = new TitleFacts(new List<string>(), null, true, true) { Name = "xXx: Return of Xander Cage" };

        Assert.Equal(TranslationKeys.RequestsErrorTitleNotRequestable, RequestService.RuleRefusal(config, Requester(), RequestType.Movie, new Dictionary<string, string> { ["Tmdb"] = "1" }, "Something harmless", facts));
    }

    // ------------------------------------------------------------------ reminders

    /// <summary>
    /// Aging is due once after the set days, and recorded so it is not sent again.
    /// True: managers hear once that a request has waited too long.
    /// False: the reminder would repeat every run, or never come.
    /// </summary>
    [Fact]
    public void AgingDue_AfterTheSetDays_IsDueOnce()
    {
        var request = Waiting(3);

        Assert.False(RequestService.AgingDue(Waiting(2.9), 3, Now));
        Assert.True(RequestService.AgingDue(request, 3, Now));
        request.AgingNotifiedUtc = Now;
        Assert.False(RequestService.AgingDue(request, 3, Now.AddDays(5)));
    }

    /// <summary>
    /// Aging is off at zero, and only for waiting requests.
    /// True: a server that turned aging off, or a decided request, never reminds.
    /// False: decided requests would remind managers.
    /// </summary>
    [Fact]
    public void AgingDue_OffOrDecided_IsNotDue()
    {
        Assert.False(RequestService.AgingDue(Waiting(30), 0, Now));
        Assert.All(
            new[] { RequestStatus.Approved, RequestStatus.Declined, RequestStatus.Available, RequestStatus.Failed, RequestStatus.PartiallyAvailable },
            status => Assert.False(RequestService.AgingDue(Waiting(30, status), 3, Now)));
    }

    /// <summary>
    /// Expiring is due on each of the last three days, once each.
    /// True: checked hourly, a request expiring after 10 days warns with 3, 2, and 1 days left.
    /// False: the warning would repeat hourly or skip a day.
    /// </summary>
    [Fact]
    public void ExpiringDue_LastThreeDays_EachOnce()
    {
        var request = Waiting(6.5);
        var sent = new List<int>();

        Assert.Null(RequestService.ExpiringDue(request, 10, Now));
        for (var hour = 0; hour <= 96; hour++)
        {
            if (RequestService.ExpiringDue(request, 10, Now.AddHours(hour)) is { } left)
            {
                sent.Add(left);
                request.ExpiringNotifiedDays = left;
            }
        }

        Assert.Equal(new[] { 3, 2, 1 }, sent);
    }

    /// <summary>
    /// Expiring counts a part day as a whole one.
    /// True: a request with 2.5 days left says 3 days.
    /// False: the warning would say fewer days than are left.
    /// </summary>
    [Fact]
    public void DaysLeft_PartDay_CountsAsWhole()
    {
        var made = Now.AddDays(-7);

        Assert.Equal(3, RequestNotifier.DaysLeft(made, 10, Now));
        Assert.Equal(3, RequestNotifier.DaysLeft(made, 10, Now.AddHours(23)));
        Assert.Equal(2, RequestNotifier.DaysLeft(made, 10, Now.AddHours(24)));
        Assert.Equal(0, RequestNotifier.DaysLeft(made, 10, Now.AddDays(3)));
    }

    /// <summary>
    /// Expiring skips a missed day rather than sending it late.
    /// True: a server off on the day with 3 left says 2 at its next check.
    /// False: a late warning would say 3 days when 2 are left.
    /// </summary>
    [Fact]
    public void ExpiringDue_MissedDay_IsSkipped()
    {
        var request = Waiting(8.5);

        Assert.Equal(2, RequestService.ExpiringDue(request, 10, Now));
        request.ExpiringNotifiedDays = 2;
        Assert.Null(RequestService.ExpiringDue(request, 10, Now.AddHours(6)));
    }

    /// <summary>
    /// Expiring needs automatic rejection and a waiting request, and says nothing past its time.
    /// True: only a request that will be rejected on its own warns.
    /// False: approved requests, or a server without auto rejection, would warn.
    /// </summary>
    [Fact]
    public void ExpiringDue_NoAutoRejectionOrDecided_IsNotDue()
    {
        Assert.Null(RequestService.ExpiringDue(Waiting(9), 0, Now));
        Assert.Null(RequestService.ExpiringDue(Waiting(9, RequestStatus.Approved), 10, Now));
        Assert.Null(RequestService.ExpiringDue(Waiting(9, RequestStatus.Declined), 10, Now));
        Assert.Null(RequestService.ExpiringDue(Waiting(10.5), 10, Now));
    }

    // ------------------------------------------------------------------ specials

    /// <summary>
    /// Specials are asked for apart from seasons, and only the same special repeats a request for specials.
    /// True: a whole show and its specials are two requests that do not block each other.
    /// False: requesting a show would block its specials, or a special twice.
    /// </summary>
    [Fact]
    public void Duplicate_Specials_AreApartFromSeasons()
    {
        var whole = Open(null);
        var specials = Open(new List<int> { 0 }, Special(3), Special(5));

        Assert.Null(RequestService.Duplicate(new[] { whole }, RequestType.Series, SimpsonsIds, new List<int> { 0 }, new List<SpecialEpisode> { Special(3) }));
        Assert.Null(RequestService.Duplicate(new[] { specials }, RequestType.Series, SimpsonsIds, null));
        Assert.Null(RequestService.Duplicate(new[] { specials }, RequestType.Series, SimpsonsIds, new List<int> { 1 }));
        Assert.Null(RequestService.Duplicate(new[] { specials }, RequestType.Series, SimpsonsIds, new List<int> { 0 }, new List<SpecialEpisode> { Special(4) }));
        Assert.Same(specials, RequestService.Duplicate(new[] { specials }, RequestType.Series, SimpsonsIds, new List<int> { 0 }, new List<SpecialEpisode> { Special(4), Special(5) }));
    }

    /// <summary>
    /// Specials are available once the library has each one, found by number or by the day it aired.
    /// True: a library numbering specials by another guide still fulfils the request.
    /// False: the request would wait for ever, or be fulfilled by seasons alone.
    /// </summary>
    [Fact]
    public void Availability_Specials_NeedEachOne()
    {
        var request = Open(new List<int> { 0 }, Special(3, aired: "2020-12-25"), Special(5, aired: "2021-12-25"));
        var counts = new SeasonCounts();
        counts.Have[1] = 10;

        Assert.Equal((RequestStatus.Approved, (List<int>?)null), RequestService.Availability(request, RequestType.Series, counts));
        counts.Specials.Add(3);
        Assert.Equal(RequestStatus.Approved, RequestService.Availability(request, RequestType.Series, counts).Status);
        counts.Specials.Add(9);
        counts.SpecialDates.Add(new DateTime(2021, 12, 25));
        var (status, have) = RequestService.Availability(request, RequestType.Series, counts);
        Assert.Equal(RequestStatus.Available, status);
        Assert.Equal(new List<int> { 0 }, have);
    }

    /// <summary>
    /// A request for specials names them, and one for seasons names its seasons.
    /// True: the queue shows which specials were asked for.
    /// False: a request for specials would read as season 0.
    /// </summary>
    [Fact]
    public void DescribeWanted_Specials_AreNamed()
    {
        Assert.Equal("Special: Christmas Special", RequestService.DescribeWanted(Open(new List<int> { 0 }, Special(3, "Christmas Special"))));
        Assert.Equal("Specials: Christmas Special, Special 5", RequestService.DescribeWanted(Open(new List<int> { 0 }, Special(3, "Christmas Special"), Special(5))));
        Assert.Equal("Seasons 1-2", RequestService.DescribeWanted(Open(new List<int> { 1, 2 })));
        Assert.Equal("All seasons", RequestService.DescribeWanted(Open(null)));
    }

    // ------------------------------------------------------------------ profiles

    /// <summary>
    /// A requester picks from the profiles their groups allow, or every one, default server first, with gone ones
    /// skipped, and names shared across servers numbered.
    /// True: the request page offers only usable, allowed profiles, never naming a server.
    /// False: a requester could pick a profile their groups do not allow, or one removed since.
    /// </summary>
    [Fact]
    public void ProfileOptions_Groups_OfferTheirAllowedProfiles()
    {
        var config = ProfileServers();
        var member = Guid.NewGuid();
        var removed = Guid.NewGuid();
        var all = TestGroups.Config(
            TestGroups.Make("4K", p => { p.Requests.MayRequest = true; p.Requests.MovieProfiles = new() { "fourk", "unfinished" }; }, member),
            TestGroups.Make("Gone", p => { p.Requests.MayRequest = true; p.Requests.MovieProfiles = new() { "gone" }; }, removed));
        all.Everyone.Permissions.Requests.MayRequest = true;
        all.Requests = config;
        RequestGrant For(Guid user, bool admin = false) => PermissionRules.For(all, user, admin);

        Assert.Equal(new[] { "fourk" }, RequestService.ProfileOptions(config, RequestType.Movie, For(member), false).Select(o => o.Id));
        Assert.Equal(new[] { "hd", "fourk" }, RequestService.ProfileOptions(config, RequestType.Movie, For(Guid.NewGuid()), false).Select(o => o.Id));
        Assert.Equal(2, RequestService.ProfileOptions(config, RequestType.Movie, For(Guid.NewGuid(), true), true).Count);
        Assert.Equal(new[] { "hd" }, RequestService.ProfileOptions(config, RequestType.Movie, For(removed), false).Select(o => o.Id));

        config.ArrServers[0].Profiles[0].Name = "4K";
        config.ArrServers[1].Profiles[0].Name = "4K";
        Assert.Equal(new[] { "4K", "4K (2)" }, RequestService.ProfileOptions(config, RequestType.Movie, For(Guid.NewGuid()), false).Select(o => o.Name));
    }

    /// <summary>
    /// New requests' profile choices start with the default server's.
    /// True: the request page's first choice is the default server's profile.
    /// False: the page would preselect a profile on another server.
    /// </summary>
    [Fact]
    public void ProfileOptions_DefaultServer_ComesFirst()
    {
        var config = new RequestSettings { ArrServers = { Server("r1", RequestType.Movie), Server("r2", RequestType.Movie, isDefault: true) } };
        var all = new PluginConfiguration { Requests = config };
        all.Everyone.Permissions.Requests.MayRequest = true;

        Assert.Equal(new[] { "r2p", "r1p" }, RequestService.ProfileOptions(config, RequestType.Movie, PermissionRules.For(all, Guid.NewGuid(), false), false).Select(o => o.Id));
    }

    // ------------------------------------------------------------------ access

    /// <summary>
    /// What a user may do comes from their groups: Everyone's, or their own group's alone, and nothing for an unknown
    /// or disabled account.
    /// True: the request page shows each user what their groups allow.
    /// False: a user would get another group's permissions, or a disabled account could request.
    /// </summary>
    [Fact]
    public void GetAccess_Groups_DecideWhatAUserMayDo()
    {
        var plain = H.AddUser("plain");
        var limited = H.AddUser("limited", permissions: p => { p.Requests.Series = false; p.Requests.MaxOpenRequests = 2; p.Requests.MaxSeasons = 3; });
        var disabled = H.AddUser("disabled");
        disabled.SetPermission(PermissionKind.IsDisabled, true);
        H.Plugin.MutateConfiguration(c => { c.Requests.AllowSpecials = true; return false; });

        var everyone = H.Access(plain);
        var own = H.Access(limited);

        Assert.True(everyone.CanRequest);
        Assert.True(everyone.Series);
        Assert.Equal(10, everyone.MaxOpenRequests);
        Assert.False(everyone.IsAdministrator);
        Assert.True(everyone.AllowSpecials);
        Assert.False(own.Series);
        Assert.Equal(2, own.MaxOpenRequests);
        Assert.Equal(3, own.MaxSeasonsPerShow);
        Assert.Null(H.Requests.GetAccess(disabled.Id));
        Assert.Null(H.Requests.GetAccess(Guid.NewGuid()));
        Assert.Null(H.Requests.GetAccess(Guid.Empty));
    }

    /// <summary>
    /// Members of a group that may manage requests act as administrators within Requests, and one that may manage
    /// issues manages issues, each apart.
    /// True: a moderator group approves requests without being a server administrator.
    /// False: managing would need a server administrator, or one permission would grant the other.
    /// </summary>
    [Fact]
    public void GetAccess_ManagingGroups_SetTheManagerFlags()
    {
        var requests = H.AddUser("requests", permissions: p => p.Requests.MayManage = true);
        var issues = H.AddUser("issues", permissions: p => p.Issues.MayManage = true);
        var admin = H.AddUser("admin", admin: true);

        Assert.True(H.Access(requests).IsAdministrator);
        Assert.False(H.Access(requests).ManagesIssues);
        Assert.False(H.Access(issues).IsAdministrator);
        Assert.True(H.Access(issues).ManagesIssues);
        Assert.True(H.Access(admin).IsAdministrator);
        Assert.True(H.Access(admin).ManagesIssues);
    }

    /// <summary>
    /// Reporting needs issues on and a group that allows it.
    /// True: the page offers reporting only where the server and the user's groups both allow it.
    /// False: a user could report while issues are off.
    /// </summary>
    [Fact]
    public void GetAccess_Issues_NeedTheToolOnAndAGroup()
    {
        var viewer = H.AddUser("viewer");
        var barred = H.AddUser("barred", permissions: p => p.Issues.MayReport = false);

        Assert.False(H.Access(viewer).IssuesEnabled);
        Assert.False(H.Access(viewer).ReportIssues);
        H.Plugin.MutateConfiguration(c => { c.Issues.Enabled = true; return false; });
        Assert.True(H.Access(viewer).IssuesEnabled);
        Assert.True(H.Access(viewer).ReportIssues);
        Assert.False(H.Access(barred).ReportIssues);
    }

    // ------------------------------------------------------------------ lifecycle

    /// <summary>
    /// A request waits for approval, under the requester's name, and is not repeated by anyone.
    /// True: one request per title waits in the queue.
    /// False: the same title would be requested twice, or approved without review.
    /// </summary>
    [Fact]
    public async Task SubmitAsync_NewRequest_WaitsAndIsNotRepeated()
    {
        var viewer = H.AddUser("viewer");
        var other = H.AddUser("other");

        var (made, error) = await Submit(viewer, 603);

        Assert.Null(error);
        Assert.Equal(RequestStatus.Pending, made!.Status);
        Assert.Equal("viewer", made.UserName);
        Assert.Equal("You already requested this.", (await Submit(viewer, 603)).Error);
        Assert.Equal("Someone already requested this.", (await Submit(other, 603)).Error);
    }

    /// <summary>
    /// The open request limit and the movie quota from the user's group are held to.
    /// True: a requester at their group's limit is refused with a reason.
    /// False: a group's limits would not hold.
    /// </summary>
    [Fact]
    public async Task SubmitAsync_LimitAndQuota_AreHeldTo()
    {
        var limited = H.AddUser("limited", permissions: p => p.Requests.MaxOpenRequests = 1);
        var quota = H.AddUser("quota", permissions: p => p.Requests.MovieQuota = 1);

        Assert.Null((await Submit(limited, 1)).Error);
        Assert.Equal("You have reached your limit of 1 open requests.", (await Submit(limited, 2)).Error);
        Assert.Null((await Submit(quota, 3)).Error);
        var (refused, error) = await Submit(quota, 4);
        Assert.Null(refused);
        Assert.NotNull(error);
    }

    /// <summary>
    /// A season cap is held to.
    /// True: a requester allowed two seasons may ask for two, not three.
    /// False: the cap would not hold.
    /// </summary>
    [Fact]
    public async Task SubmitAsync_SeasonCap_IsHeldTo()
    {
        var capped = H.AddUser("capped", permissions: p => p.Requests.MaxSeasons = 2);
        var show = Show(1, 2, 3);

        Assert.Equal("You can request up to 2 seasons of a show.", (await H.Requests.SubmitAsync(H.Access(capped), show, CancellationToken.None)).Error);
        show.Seasons = new() { 1, 2 };
        Assert.Null((await H.Requests.SubmitAsync(H.Access(capped), show, CancellationToken.None)).Error);
    }

    /// <summary>
    /// A season cap counts only the requester's own requests, open or in the library, and not rejected ones.
    /// True: someone else's request for the show does not use up the cap.
    /// False: one requester's seasons would count against another.
    /// </summary>
    [Fact]
    public async Task SubmitAsync_SeasonCap_CountsOnlyTheirOwn()
    {
        var capped = H.AddUser("capped", permissions: p => p.Requests.MaxSeasons = 2);
        var other = H.AddUser("other");

        Assert.Null((await H.Requests.SubmitAsync(H.Access(other), Show(3, 4, 5), CancellationToken.None)).Error);
        var mine = (await H.Requests.SubmitAsync(H.Access(capped), Show(1, 2), CancellationToken.None)).Request!;
        Assert.NotNull(mine);
        Assert.Equal("You can request up to 2 seasons of a show.", (await H.Requests.SubmitAsync(H.Access(capped), Show(6), CancellationToken.None)).Error);

        var fulfilled = H.Db.Get(mine.Id)!;
        fulfilled.Status = RequestStatus.Available;
        H.Db.Update(fulfilled);
        Assert.Equal("You can request up to 2 seasons of a show.", (await H.Requests.SubmitAsync(H.Access(capped), Show(6), CancellationToken.None)).Error);

        fulfilled.Status = RequestStatus.Declined;
        H.Db.Update(fulfilled);
        Assert.Null((await H.Requests.SubmitAsync(H.Access(capped), Show(6, 7), CancellationToken.None)).Error);
    }

    /// <summary>
    /// A request from a group that auto approves movies is approved and added to Radarr with its profile.
    /// True: a trusted requester's movie goes straight to Radarr in the right quality and folder.
    /// False: it would wait for approval, or reach Radarr without its profile.
    /// </summary>
    [Fact]
    public async Task SubmitAsync_AutoApproved_IsAddedToRadarr()
    {
        H.UseRadarr();
        var trusted = H.AddUser("trusted", permissions: p => p.Requests.AutoApproveMovies = true);

        var (made, error) = await Submit(trusted, 603);

        Assert.Null(error);
        Assert.Equal(RequestStatus.Approved, made!.Status);
        Assert.Equal(100, made.ArrId);
        var added = H.RadarrPosts.First(p => p.Path == "/api/v3/movie").Body!;
        Assert.Equal(603, added["tmdbId"]!.GetValue<int>());
        Assert.Equal(4, added["qualityProfileId"]!.GetValue<int>());
        Assert.Equal("/movies", added["rootFolderPath"]!.GetValue<string>());
    }

    /// <summary>
    /// A manager approves or rejects, and a rejected request is not approved after all.
    /// True: the decision and who made it are kept, with the reason given.
    /// False: a rejected request could be sent, or the decision lost.
    /// </summary>
    [Fact]
    public async Task ApproveAsync_AndDecline_AreKept()
    {
        var viewer = H.AddUser("viewer");
        var first = (await Submit(viewer, 1)).Request!;
        var second = (await Submit(viewer, 2)).Request!;

        var approved = await H.Requests.ApproveAsync(first.Id, "admin", CancellationToken.None, waitFor: SendWait);
        var declined = H.Requests.Decline(second.Id, "admin", "Not on this server");

        Assert.Equal(RequestStatus.Approved, approved!.Status);
        Assert.Equal("admin", approved.DecidedBy);
        Assert.Null(approved.ArrId);
        Assert.Equal(RequestStatus.Declined, declined!.Status);
        Assert.Equal("Not on this server", declined.Note);
        Assert.Null(await H.Requests.ApproveAsync(second.Id, "admin", CancellationToken.None, waitFor: SendWait));
    }

    /// <summary>
    /// Each decision sends its event to Notifications, about the requester.
    /// True: Notifications hears every new, approved, and rejected request.
    /// False: a requester would never hear what happened to their request.
    /// </summary>
    [Fact]
    public async Task ApproveAsync_AndDecline_SendTheirEvents()
    {
        var viewer = H.AddUser("viewer");
        var first = (await Submit(viewer, 1)).Request!;
        var second = (await Submit(viewer, 2)).Request!;

        await H.Requests.ApproveAsync(first.Id, "admin", CancellationToken.None, waitFor: SendWait);
        H.Requests.Decline(second.Id, "admin", "Not on this server");

        Assert.Equal(new[] { RequestEvents.Created, RequestEvents.Created, RequestEvents.Approved, RequestEvents.Declined }, H.Events.ChannelKeys);
        Assert.All(H.Events.Sent, e => Assert.Equal(viewer.Id, e.UserId));
    }

    /// <summary>
    /// Activity entries name who did what to whose request, without the requester's note.
    /// True: the log reads Admin approved viewer's request for Movie 1 (2020).
    /// False: the log would lose who acted, or show a private note.
    /// </summary>
    [Fact]
    public async Task ApproveAsync_Activity_NamesWhoAndWhoseWithoutTheNote()
    {
        var viewer = H.AddUser("viewer");
        var admin = H.AddUser("admin", admin: true);
        var movie = Movie(1);
        movie.Note = "For movie night.";
        var first = (await H.Requests.SubmitAsync(H.Access(viewer), movie, CancellationToken.None, SendWait)).Request!;
        var second = (await Submit(viewer, 2)).Request!;
        var third = (await Submit(viewer, 3)).Request!;
        lock (H.Activity.Entries)
        {
            H.Activity.Entries.Clear();
        }

        await H.Requests.ApproveAsync(first.Id, "admin", CancellationToken.None, waitFor: SendWait);
        H.Requests.Decline(second.Id, "admin", "Not on this server");
        H.Requests.Remove(third.Id, H.Access(admin));

        Assert.Equal(
            new[]
            {
                ("Request approved", "Admin approved viewer's request for Movie 1 (2020)"),
                ("Request rejected", "Admin rejected viewer's request for Movie 2 (2020)"),
                ("Request removed", "Admin removed viewer's request for Movie 3 (2020)")
            },
            H.Activity.Entries.Select(e => (e.Name, e.ShortOverview ?? string.Empty)).ToArray());
        Assert.All(H.Activity.Entries, e => Assert.Equal(viewer.Id, e.UserId));
    }

    /// <summary>
    /// A request is logged without its note.
    /// True: the log says who requested what, and nothing private.
    /// False: a requester's note would show in the activity log.
    /// </summary>
    [Fact]
    public async Task SubmitAsync_Activity_LeavesOutTheNote()
    {
        var viewer = H.AddUser("viewer");
        var movie = Movie(1);
        movie.Note = "For movie night.";

        await H.Requests.SubmitAsync(H.Access(viewer), movie, CancellationToken.None, SendWait);

        var entry = Assert.Single(H.Activity.Entries);
        Assert.Equal(("Request created", "Viewer requested Movie 1 (2020)"), (entry.Name, entry.ShortOverview));
    }

    /// <summary>
    /// A send Radarr refuses fails once, backs off, and sends one failed event.
    /// True: a refused request is retried later and managers hear of it once.
    /// False: a refusal would be retried at once, or announced repeatedly.
    /// </summary>
    [Fact]
    public async Task ApproveAsync_RadarrRefuses_FailsOnceAndBacksOff()
    {
        H.UseRadarr();
        var viewer = H.AddUser("viewer");
        var request = (await Submit(viewer, 603)).Request!;
        H.RadarrFails = HttpStatusCode.Unauthorized;

        var failed = await H.Requests.ApproveAsync(request.Id, "admin", CancellationToken.None, waitFor: SendWait);

        Assert.Equal(RequestStatus.Failed, failed!.Status);
        Assert.Equal(1, failed.SendFailures);
        Assert.NotNull(failed.RetryAfterUtc);
        Assert.Single(H.Events.ChannelKeys, k => k == RequestEvents.Failed);
    }

    /// <summary>
    /// Radarr blocks only what it looks after, and a movie it has is monitored rather than added again.
    /// True: a movie Radarr let go can be requested and is searched, not duplicated.
    /// False: a dropped movie could not be requested, or would be added twice.
    /// </summary>
    [Fact]
    public async Task SubmitAsync_RadarrHasIt_BlocksOnlyWhatItLooksAfter()
    {
        H.UseRadarr();
        var viewer = H.AddUser("viewer");
        H.RadarrMovies[1] = new JsonObject { ["id"] = 7, ["tmdbId"] = 1, ["title"] = "Watched", ["monitored"] = true, ["hasFile"] = false };
        H.RadarrMovies[2] = new JsonObject { ["id"] = 8, ["tmdbId"] = 2, ["title"] = "Let Go", ["monitored"] = false, ["hasFile"] = false };

        Assert.Equal("This is already waiting to download.", (await Submit(viewer, 1)).Error);
        var (made, error) = await Submit(viewer, 2);
        Assert.Null(error);

        await H.Requests.ApproveAsync(made!.Id, "admin", CancellationToken.None, waitFor: SendWait);
        Assert.Equal(8, H.Db.Get(made.Id)!.ArrId);
        Assert.DoesNotContain(H.RadarrPosts, p => p.Path == "/api/v3/movie");
    }

    /// <summary>
    /// A title in the library fulfils its request, and one replaced by a better copy is announced once.
    /// True: the requester hears once that their title arrived.
    /// False: an upgrade would announce the title again.
    /// </summary>
    [Fact]
    public async Task Fulfill_TitleInTheLibrary_IsAnnouncedOnce()
    {
        var viewer = H.AddUser("viewer");
        var request = (await Submit(viewer, 603)).Request!;
        await H.Requests.ApproveAsync(request.Id, "admin", CancellationToken.None, waitFor: SendWait);
        var movie = new Movie { Id = Guid.NewGuid(), Name = "Movie 603", ProductionYear = 2020, ProviderIds = new() { ["Tmdb"] = "603" } };
        var upgraded = new Movie { Id = Guid.NewGuid(), Name = "Movie 603", ProductionYear = 2020, ProviderIds = new() { ["Tmdb"] = "603" } };

        H.Library.Add(movie);
        var fulfilled = Assert.Single(H.Requests.Fulfill(movie));
        H.Library.Clear();
        var gone = Assert.Single(H.Requests.Recheck(_ => true));
        H.Library.Add(upgraded);
        var back = Assert.Single(H.Requests.Fulfill(upgraded));

        Assert.Equal(RequestStatus.Available, fulfilled.Status);
        Assert.Equal(movie.Id, fulfilled.LibraryItemId);
        Assert.Equal(RequestStatus.Approved, gone.Status);
        Assert.Equal(RequestStatus.Available, back.Status);
        Assert.Single(H.Events.ChannelKeys, k => k == RequestEvents.Available);
    }

    /// <summary>
    /// A removed item rechecks only the requests it fulfilled, once.
    /// True: one title leaving the library reopens its own request alone.
    /// False: a removal would reopen every request, or the same one twice.
    /// </summary>
    [Fact]
    public async Task Recheck_RemovedItem_RechecksOnlyItsRequests()
    {
        var viewer = H.AddUser("viewer");
        var kept = (await Submit(viewer, 1)).Request!;
        var lost = (await Submit(viewer, 2)).Request!;
        var keptMovie = new Movie { Id = Guid.NewGuid(), Name = "Movie 1", ProductionYear = 2020, ProviderIds = new() { ["Tmdb"] = "1" } };
        var lostMovie = new Movie { Id = Guid.NewGuid(), Name = "Movie 2", ProductionYear = 2020, ProviderIds = new() { ["Tmdb"] = "2" } };
        H.Library.Add(keptMovie);
        H.Library.Add(lostMovie);
        Assert.Single(H.Requests.Fulfill(keptMovie));
        Assert.Single(H.Requests.Fulfill(lostMovie));

        H.Library.Clear();

        Assert.Empty(H.Requests.Recheck(Array.Empty<Guid>()));
        Assert.Empty(H.Requests.Recheck(new[] { Guid.NewGuid() }));
        var changed = Assert.Single(H.Requests.Recheck(new[] { lostMovie.Id }));
        Assert.Equal(lost.Id, changed.Id);
        Assert.Equal(RequestStatus.Pending, changed.Status);
        Assert.Null(changed.LibraryItemId);
        Assert.Equal(RequestStatus.Available, H.Db.Get(kept.Id)!.Status);
        Assert.Empty(H.Requests.Recheck(new[] { lostMovie.Id }));
    }

    /// <summary>
    /// The service's open requests are the database's kept list, read again after a new request.
    /// True: search and the sweep share one list.
    /// False: the service would read the open requests on every call, or miss a new one.
    /// </summary>
    [Fact]
    public async Task OpenRequests_Service_AreTheKeptList()
    {
        var viewer = H.AddUser("viewer");
        await Submit(viewer, 1);

        var open = H.Requests.OpenRequests();
        Assert.Same(H.Db.OpenRequests(), open);
        Assert.Same(open, H.Requests.OpenRequests());

        await Submit(viewer, 2);
        Assert.NotSame(open, H.Requests.OpenRequests());
        Assert.Equal(2, H.Requests.OpenRequests().Count);
    }

    /// <summary>
    /// A change that changes nothing is not written.
    /// True: a no-op change keeps the open list and the request's update time.
    /// False: every sync would rewrite every request and rebuild the list.
    /// </summary>
    [Fact]
    public async Task Change_NothingChanged_IsNotWritten()
    {
        var viewer = H.AddUser("viewer");
        var request = (await Submit(viewer, 1)).Request!;
        var change = typeof(RequestService).GetMethod("Change", BindingFlags.NonPublic | BindingFlags.Instance)!;
        MediaRequest? Change(Action<MediaRequest> what) => (MediaRequest?)change.Invoke(H.Requests, new object[] { request.Id, (Func<MediaRequest, bool>)(_ => true), what, true });
        var open = H.Db.OpenRequests();
        var stamped = H.Db.Get(request.Id)!.UpdatedUtc;

        Assert.NotNull(Change(r => r.Note = r.Note));
        Assert.Same(open, H.Db.OpenRequests());
        Assert.Equal(stamped, H.Db.Get(request.Id)!.UpdatedUtc);

        Assert.Equal("Changed", Change(r => r.Note = "Changed")!.Note);
        Assert.NotSame(open, H.Db.OpenRequests());
        Assert.Equal("Changed", H.Db.Get(request.Id)!.Note);
        Assert.True(H.Db.Get(request.Id)!.UpdatedUtc >= stamped);
    }

    /// <summary>
    /// Only a pending request is the requester's to remove, and a manager may remove any.
    /// True: a requester can take back a request not yet sent.
    /// False: a requester could pull a sent request, or a manager could not clear one.
    /// </summary>
    [Fact]
    public async Task Remove_Pending_IsTheRequesters()
    {
        var viewer = H.AddUser("viewer");
        var admin = H.AddUser("admin", admin: true);
        var pending = (await Submit(viewer, 1)).Request!;
        var approved = (await Submit(viewer, 2)).Request!;
        await H.Requests.ApproveAsync(approved.Id, "admin", CancellationToken.None, waitFor: SendWait);

        Assert.True(H.Requests.Remove(pending.Id, H.Access(viewer)));
        Assert.Null(H.Db.Get(pending.Id));
        Assert.False(H.Requests.Remove(approved.Id, H.Access(viewer)));
        Assert.True(H.Requests.Remove(approved.Id, H.Access(admin)));
    }

    /// <summary>
    /// Someone else may join a request and leave it, and the requester may not join their own.
    /// True: others who want the title follow the one request.
    /// False: the same title would be requested again, or a requester would join their own.
    /// </summary>
    [Fact]
    public async Task Join_AndLeave_AreKept()
    {
        var owner = H.AddUser("owner");
        var joiner = H.AddUser("joiner");
        var request = (await Submit(owner, 603)).Request!;

        Assert.Equal("This is your own request.", H.Requests.Join(H.Access(owner), request.Id, true).Error);
        var joined = H.Requests.Join(H.Access(joiner), request.Id, true).Request!;
        Assert.Equal(joiner.Id, Assert.Single(joined.Joined!).UserId);
        Assert.Null(H.Requests.Join(H.Access(joiner), request.Id, false).Request!.Joined);
    }

    /// <summary>
    /// A request for a title in a library hidden from someone cannot be joined by them, and can by one who sees it.
    /// True: joining never tells a user about a title they cannot see.
    /// False: a hidden library's title would be revealed through its request.
    /// </summary>
    [Fact]
    public async Task Join_TitleHiddenFromThem_IsRefused()
    {
        var owner = H.AddUser("owner");
        var outsider = H.AddUser("outsider");
        var admin = H.AddUser("admin", admin: true);
        var request = (await Submit(owner, 603)).Request!;
        var movie = new Movie { Id = Guid.NewGuid(), Name = "Movie 603", Path = "/hidden/movie.mkv", ProviderIds = new() { ["Tmdb"] = "603" } };
        H.Library.Add(movie);
        H.LibraryOf[movie.Id] = Guid.NewGuid();
        var stored = H.Db.Get(request.Id)!;
        stored.LibraryItemId = movie.Id;
        H.Db.Update(stored);

        Assert.False(H.Requests.CanSee(movie.Id, H.Access(outsider)));
        Assert.Null(H.Requests.Join(H.Access(outsider), request.Id, true).Request);
        Assert.Null(H.Db.Get(request.Id)!.Joined);
        Assert.NotNull(H.Requests.Join(H.Access(admin), request.Id, true).Request);
    }

    /// <summary>
    /// A removed request passes to the first joiner who may hold it.
    /// True: others still waiting keep the request when its requester leaves.
    /// False: the request would vanish for everyone who joined it.
    /// </summary>
    [Fact]
    public async Task Remove_WithAJoiner_HandsTheRequestOn()
    {
        var owner = H.AddUser("owner");
        var joiner = H.AddUser("joiner");
        var request = (await Submit(owner, 603)).Request!;
        H.Requests.Join(H.Access(joiner), request.Id, true);

        Assert.True(H.Requests.Remove(request.Id, H.Access(owner)));

        var handed = H.Db.Get(request.Id)!;
        Assert.Equal(joiner.Id, handed.UserId);
        Assert.Null(handed.Joined);
    }

    /// <summary>
    /// A removed request is not handed past the joiner's limits.
    /// True: a joiner at their group's limit does not inherit a request past it.
    /// False: handing on would put a joiner over their limit.
    /// </summary>
    [Fact]
    public async Task Remove_JoinerAtTheirLimit_RemovesInstead()
    {
        var owner = H.AddUser("owner");
        var full = H.AddUser("full", permissions: p => p.Requests.MaxOpenRequests = 1);
        Assert.Null((await Submit(full, 1)).Error);
        var request = (await Submit(owner, 603)).Request!;
        H.Requests.Join(H.Access(full), request.Id, true);

        Assert.True(H.Requests.Remove(request.Id, H.Access(owner)));

        Assert.Null(H.Db.Get(request.Id));
    }

    /// <summary>
    /// A handed request moves to a profile the new requester's groups allow.
    /// True: a 4K request handed to someone allowed only HD becomes an HD request.
    /// False: a joiner would inherit a profile their groups do not allow.
    /// </summary>
    [Fact]
    public async Task Remove_HandedOn_MovesToAnAllowedProfile()
    {
        H.UseRadarr(new ArrProfileOption { Id = "uhd", Name = "4K", QualityProfileId = 5, RootFolder = "/movies4k" });
        var owner = H.AddUser("owner");
        var joiner = H.AddUser("joiner", permissions: p => p.Requests.MovieProfiles = new() { "hd" });
        var request = (await H.Requests.SubmitAsync(H.Access(owner), Movie(603, "uhd"), CancellationToken.None)).Request!;
        Assert.Equal("uhd", request.ProfileId);
        H.Requests.Join(H.Access(joiner), request.Id, true);

        H.Requests.Remove(request.Id, H.Access(owner));

        var handed = H.Db.Get(request.Id)!;
        Assert.Equal(joiner.Id, handed.UserId);
        Assert.Equal("hd", handed.ProfileId);
    }

    /// <summary>
    /// With whole shows only, a season cannot be picked, and the whole show can.
    /// True: a server that wants whole shows gets only whole show requests.
    /// False: the setting would not hold.
    /// </summary>
    [Fact]
    public async Task SubmitAsync_FullSeriesOnly_RefusesASeason()
    {
        H.Plugin.MutateConfiguration(c => { c.Requests.FullSeriesOnly = true; return false; });

        var (picked, error) = await Simpsons(new List<int> { 1 });
        var (whole, none) = await Simpsons(null);

        Assert.Null(picked);
        Assert.Equal("Only whole shows can be requested.", error);
        Assert.Null(none);
        Assert.NotNull(whole);
        Assert.Null(whole!.Seasons);
    }

    /// <summary>
    /// Without whole shows only, a season can be picked.
    /// True: a viewer may ask for the season they want.
    /// False: every show request would be the whole show.
    /// </summary>
    [Fact]
    public async Task SubmitAsync_SeasonsAllowed_KeepsTheSeason()
    {
        var (picked, error) = await Simpsons(new List<int> { 1 });

        Assert.Null(error);
        Assert.Equal(new List<int> { 1 }, picked!.Seasons);
    }

    /// <summary>
    /// Specials cannot be requested unless allowed, and never together with seasons.
    /// True: specials are a request of their own on servers that allow them.
    /// False: specials could be requested against the setting, or mixed with seasons.
    /// </summary>
    [Fact]
    public async Task SubmitAsync_Specials_NeedTheSetting()
    {
        var (request, error) = await Simpsons(null, new List<int> { 3 });

        Assert.Null(request);
        Assert.Equal("Specials cannot be requested.", error);
        H.Plugin.MutateConfiguration(c => { c.Requests.AllowSpecials = true; return false; });
        Assert.Equal("Those specials are not valid.", (await Simpsons(new List<int> { 1 }, new List<int> { 3 })).Error);
    }

    private Task<(MediaRequest? Request, string? Error)> Submit(User user, int tmdb)
        => H.Requests.SubmitAsync(H.Access(user), Movie(tmdb), CancellationToken.None, SendWait);

    private Task<(MediaRequest? Request, string? Error)> Simpsons(List<int>? seasons, List<int>? specials = null)
        => H.Requests.SubmitAsync(H.Access(H.AddUser("viewer")), new NewMediaRequest
        {
            Type = RequestType.Series,
            Title = "The Simpsons",
            Year = 1989,
            ProviderIds = new(StringComparer.OrdinalIgnoreCase) { [ProviderIds.Tmdb] = "456", [ProviderIds.Tvdb] = "71663" },
            Seasons = seasons,
            Specials = specials
        }, CancellationToken.None);

    private static NewMediaRequest Movie(int tmdb, string? profile = null)
        => new() { Type = RequestType.Movie, Title = "Movie " + tmdb, Year = 2020, ProviderIds = new() { ["Tmdb"] = tmdb.ToString(System.Globalization.CultureInfo.InvariantCulture) }, ProfileId = profile };

    private static NewMediaRequest Show(params int[] seasons)
        => new() { Type = RequestType.Series, Title = "Show", Year = 2020, ProviderIds = new() { ["Tvdb"] = "81189" }, Seasons = seasons.ToList() };

    private static UserAccess Requester(bool unreleased = true)
    {
        var config = new PluginConfiguration();
        config.Everyone.Permissions.Requests.MayRequest = true;
        config.Everyone.Permissions.Requests.Unreleased = unreleased;
        return new UserAccess { UserId = Guid.NewGuid(), Permissions = PermissionRules.For(config, Guid.NewGuid(), false) };
    }

    private static MediaRequest Waiting(double daysAgo, RequestStatus status = RequestStatus.Pending) => new()
    {
        Id = Guid.NewGuid(),
        Title = "Dune",
        Year = 2021,
        UserName = "viewer",
        Status = status,
        CreatedUtc = Now.AddDays(-daysAgo),
        UpdatedUtc = Now.AddDays(-daysAgo)
    };

    private static MediaRequest Sent(Guid user) => new()
    {
        Id = Guid.NewGuid(),
        Type = RequestType.Movie,
        Title = "The Matrix",
        ProviderIds = new Dictionary<string, string>(MatrixIds, StringComparer.OrdinalIgnoreCase),
        UserId = user,
        Status = RequestStatus.Approved,
        ArrId = 12,
        ServerId = "radarr"
    };

    private static SpecialEpisode Special(int number, string? name = null, string? aired = null)
        => new() { Number = number, Name = name, AirDate = aired is null ? null : DateTime.Parse(aired, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal) };

    private static MediaRequest Open(List<int>? seasons, params SpecialEpisode[] specials)
        => new() { Type = RequestType.Series, Status = RequestStatus.Approved, ProviderIds = new(SimpsonsIds, StringComparer.OrdinalIgnoreCase), Seasons = seasons, Specials = specials.Length > 0 ? specials.ToList() : null };

    // Episodes in the library by season, and the seasons the guide marks as missing episodes.
    private static SeasonCounts Counts(Dictionary<int, int> have, params int[] missing)
    {
        var counts = new SeasonCounts();
        foreach (var (season, episodes) in have)
        {
            counts.Have[season] = episodes;
        }

        foreach (var season in missing)
        {
            counts.Missing.Add(season);
        }

        return counts;
    }

    private static ArrServer Server(string id, RequestType type, bool isDefault = false)
        => new()
        {
            Id = id,
            Type = type,
            Url = "http://" + id,
            ApiKey = "k",
            IsDefault = isDefault,
            Profiles = { new ArrProfileOption { Id = id + "p", QualityProfileId = 1, RootFolder = "/media" } }
        };

    private static RequestSettings ProfileServers() => new()
    {
        ArrServers =
        {
            new ArrServer { Id = "main", Type = RequestType.Movie, Url = "http://radarr", ApiKey = "k", Profiles = { new ArrProfileOption { Id = "hd", QualityProfileId = 1, RootFolder = "/movies" } } },
            new ArrServer { Id = "uhd", Type = RequestType.Movie, Url = "http://radarr4k", ApiKey = "k", Profiles = { new ArrProfileOption { Id = "fourk", QualityProfileId = 5, RootFolder = "/4k" }, new ArrProfileOption { Id = "unfinished" } } },
            new ArrServer { Id = "tv", Type = RequestType.Series, Url = "http://sonarr", ApiKey = string.Empty, Profiles = { new ArrProfileOption { Id = "nokey", QualityProfileId = 1, RootFolder = "/tv" } } }
        }
    };
}
