using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Necto.Api.Requests;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities.Movies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Siphonophore.Stolon;
using Xunit;
using SearchResult = Jellyfin.Plugin.Necto.Services.Requests.Models.SearchResult;

namespace Jellyfin.Plugin.Necto.Tests.Api.Requests;

/// <summary>
/// The Requests controller acts for the right user, shows each viewer only what they may see, keeps managers' routes to
/// managers, and serves the request page with its script, styles, manifest, and service worker. It reads the loaded
/// plugin's settings, so it runs on its own.
/// </summary>
[Collection(ServicesCollection.Name)]
public sealed class RequestsControllerTests : IDisposable
{
    private RequestHarness? _harness;

    private RequestHarness H => _harness ??= new RequestHarness();

    public void Dispose() => _harness?.Dispose();

    // ------------------------------------------------------------------ who the API acts as

    /// <summary>
    /// Someone else's request is reached only by a viewer whose groups let them see others' requests.
    /// True: a request stays private to its requester unless a group shares it.
    /// False: anyone could read anyone's request by its id.
    /// </summary>
    [Fact]
    public async Task One_SomeoneElsesRequest_NeedsAGroupThatSeesOthers()
    {
        var owner = H.AddUser("owner");
        var plain = H.AddUser("plain");
        var sees = H.AddUser("sees", permissions: p => p.Requests.MaySeeOthers = true);
        var request = await Submit(owner, 603);

        Assert.IsType<NotFoundResult>(H.Api(plain).One(request.Id).Result);
        Assert.IsType<OkObjectResult>(H.Api(sees).One(request.Id).Result);
        Assert.IsType<OkObjectResult>(H.Api(owner).One(request.Id).Result);
    }

    /// <summary>
    /// A user who is not a manager naming another user still acts as themselves.
    /// True: userId in the address never lets a requester act for someone else.
    /// False: anyone could remove another's request by naming them.
    /// </summary>
    [Fact]
    public async Task Remove_NamingAnotherUser_StillActsAsThemselves()
    {
        var owner = H.AddUser("owner");
        var other = H.AddUser("other");
        var request = await Submit(owner, 603);

        Assert.IsType<NotFoundResult>(H.Api(other, "?userId=" + owner.Id.ToString("N")).Remove(request.Id));
        Assert.NotNull(H.Db.Get(request.Id));
    }

    /// <summary>
    /// An administrator or an API key acts for the user named, and an administrator naming no one acts as themselves.
    /// True: an integration lists a user's requests by naming them.
    /// False: an API key could not reach a user's requests, or an administrator would see another's as their own.
    /// </summary>
    [Fact]
    public async Task Mine_AdministratorOrApiKey_ActsForTheUserNamed()
    {
        var owner = H.AddUser("owner");
        var admin = H.AddUser("admin", admin: true);
        var request = await Submit(owner, 603);

        var mine = Page(H.Api(admin, "?userId=" + owner.Id.ToString("N")).Mine(new PagedQuery(), null, null, null, null));
        var keyed = Page(H.Api(null, "?userId=" + owner.Id.ToString("N")).Mine(new PagedQuery(), null, null, null, null));

        Assert.Equal(request.Id, Assert.Single(mine.Items).RequestId);
        Assert.Empty(Page(H.Api(admin).Mine(new PagedQuery(), null, null, null, null)).Items);
        Assert.Equal(request.Id, Assert.Single(keyed.Items).RequestId);
    }

    /// <summary>
    /// Everyone's requests need a group that sees others' requests.
    /// True: the All list is refused to plain requesters.
    /// False: any requester could list every request on the server.
    /// </summary>
    [Fact]
    public void All_WithoutSeeingOthers_IsForbidden()
    {
        var plain = H.AddUser("plain");
        var sees = H.AddUser("sees", permissions: p => p.Requests.MaySeeOthers = true);

        Assert.IsType<ForbidResult>(H.Api(plain).All(new PagedQuery(), null, null, null, null, null).Result);
        Assert.IsType<OkObjectResult>(H.Api(sees).All(new PagedQuery(), null, null, null, null, null).Result);
    }

    // ------------------------------------------------------------------ manager routes

    /// <summary>
    /// Every route that decides or manages requests is marked for request managers, and every issue decision for issue managers.
    /// True: the filter checks each of them before it runs.
    /// False: a requester could approve, decline, or decide an issue by calling the route.
    /// </summary>
    [Theory]
    [InlineData(nameof(RequestsController.Approve), typeof(ManagesRequestsAttribute))]
    [InlineData(nameof(RequestsController.Decline), typeof(ManagesRequestsAttribute))]
    [InlineData(nameof(RequestsController.MarkAvailable), typeof(ManagesRequestsAttribute))]
    [InlineData(nameof(RequestsController.Bulk), typeof(ManagesRequestsAttribute))]
    [InlineData(nameof(RequestsController.Refresh), typeof(ManagesRequestsAttribute))]
    [InlineData(nameof(RequestsController.Requesters), typeof(ManagesRequestsAttribute))]
    [InlineData(nameof(RequestsController.Profiles), typeof(ManagesRequestsAttribute))]
    [InlineData(nameof(RequestsController.GetArrLink), typeof(ManagesRequestsAttribute))]
    [InlineData(nameof(RequestsController.ListIssues), typeof(ManagesIssuesAttribute))]
    [InlineData(nameof(RequestsController.GetIssue), typeof(ManagesIssuesAttribute))]
    [InlineData(nameof(RequestsController.DecideIssue), typeof(ManagesIssuesAttribute))]
    [InlineData(nameof(RequestsController.IssueArrLink), typeof(ManagesIssuesAttribute))]
    public void ManagerRoutes_AreMarked(string action, Type attribute)
    {
        var method = typeof(RequestsController).GetMethod(action)!;

        Assert.NotNull(method.GetCustomAttribute(attribute));
    }

    /// <summary>
    /// A marked route takes administrators and members of a group that may manage, and forbids anyone else.
    /// True: a moderator group decides requests, and a plain requester is refused before the route runs.
    /// False: managing would need a server administrator, or anyone could manage.
    /// </summary>
    [Fact]
    public void OnActionExecuting_ManagerRoute_ChecksTheCallersGroups()
    {
        var plain = H.AddUser("plain");
        var requests = H.AddUser("requests", permissions: p => p.Requests.MayManage = true);
        var issues = H.AddUser("issues", permissions: p => p.Issues.MayManage = true);
        var admin = H.AddUser("admin", admin: true);

        Assert.IsType<ForbidResult>(Filter(plain, new ManagesRequestsAttribute()));
        Assert.Null(Filter(requests, new ManagesRequestsAttribute()));
        Assert.IsType<ForbidResult>(Filter(requests, new ManagesIssuesAttribute()));
        Assert.Null(Filter(issues, new ManagesIssuesAttribute()));
        Assert.IsType<ForbidResult>(Filter(issues, new ManagesRequestsAttribute()));
        Assert.Null(Filter(admin, new ManagesRequestsAttribute()));
        Assert.Null(Filter(admin, new ManagesIssuesAttribute()));
        Assert.Null(Filter(plain));
    }

    // ------------------------------------------------------------------ what a viewer sees

    /// <summary>
    /// A viewer sees a copy without Radarr's and Sonarr's details, servers' names, others' notes, or hidden requesters.
    /// True: requesters never learn the servers' setup or another person's private note.
    /// False: a request would leak a server's name, an error, or who asked for it.
    /// </summary>
    [Fact]
    public void ForViewer_Requester_SeesNothingOfTheServers()
    {
        var request = new MediaRequest
        {
            Status = RequestStatus.Failed, ArrId = 7, Note = "Radarr refused the API key.", ProgressText = "Sent to Radarr", SendFailures = 2,
            RetryAfterUtc = DateTime.UtcNow.AddHours(1), ExpectedEpisodes = new() { [1] = 10 }
        };
        var theirs = new MediaRequest
        {
            UserId = Guid.NewGuid(), UserName = "Jane", RequesterNote = "for the kids", ServerId = "s1", ProfileId = "uhd",
            Status = RequestStatus.Declined, Note = "Jane, you have this on disc", DecidedBy = "Jane"
        };

        var seen = RequestsController.ForViewer(request, new UserAccess());
        var hidden = RequestsController.ForViewer(theirs, new UserAccess { UserId = Guid.NewGuid() });
        var declinedOwn = RequestsController.ForViewer(new MediaRequest { UserId = theirs.UserId, UserName = "Jane", Status = RequestStatus.Declined, DecidedBy = "Jane" }, new UserAccess { UserId = Guid.NewGuid() });
        var approvedOwn = RequestsController.ForViewer(new MediaRequest { UserId = theirs.UserId, UserName = "Jane", Status = RequestStatus.Approved, DecidedBy = "Jane" }, new UserAccess { UserId = Guid.NewGuid() });
        var named = RequestsController.ForViewer(theirs, new UserAccess { UserId = Guid.NewGuid(), SeeRequesters = true });
        var own = RequestsController.ForViewer(theirs, new UserAccess { UserId = theirs.UserId });
        var admin = new UserAccess { IsAdministrator = true };

        Assert.Null(seen.ArrId);
        Assert.Null(seen.Note);
        Assert.Equal(0, seen.SendFailures);
        Assert.Null(seen.RetryAfterUtc);
        Assert.Null(seen.ExpectedEpisodes);
        Assert.Equal(7, request.ArrId);
        Assert.Equal(2, request.SendFailures);
        Assert.NotNull(request.RetryAfterUtc);
        Assert.NotNull(request.ExpectedEpisodes);
        Assert.Equal("Radarr refused the API key.", request.Note);
        Assert.Equal("Sent for download", seen.ProgressText);
        Assert.Equal(string.Empty, hidden.UserName);
        Assert.Equal(Guid.Empty, hidden.UserId);
        Assert.Null(hidden.RequesterNote);
        Assert.Null(hidden.Note);
        Assert.Null(hidden.ServerId);
        Assert.Null(hidden.ProfileId);
        Assert.Null(hidden.DecidedBy);
        Assert.Null(declinedOwn.DecidedBy);
        Assert.Equal(string.Empty, declinedOwn.UserName);
        Assert.Equal("Approved automatically", approvedOwn.DecidedBy);
        Assert.Equal("Jane", named.UserName);
        Assert.Equal("Jane", named.DecidedBy);
        Assert.Null(named.Note);
        Assert.Equal("for the kids", own.RequesterNote);
        Assert.Equal("Jane, you have this on disc", own.Note);
        Assert.Equal("uhd", own.ProfileId);
        Assert.Null(own.ServerId);
        Assert.Equal("Sent for download", RequestsController.ForViewer(request, admin).ProgressText);
        Assert.Equal("Sent to Radarr", request.ProgressText);
        request.ProgressText = "Downloading 40%";
        Assert.Same(request, RequestsController.ForViewer(request, admin));
    }

    /// <summary>
    /// Progress text loses servers' own names, even ones renamed or removed since, and ordinary text is untouched.
    /// True: a requester reads Sent for download, never the name of a 4K Radarr.
    /// False: a server's name would reach requesters, or plain progress would be rewritten.
    /// </summary>
    [Theory]
    [InlineData("Download failed, Sonarr will try another release", "Retrying download")]
    [InlineData("Sent to 4K Radarr", "Sent for download")]
    [InlineData("Not in Movies HD yet", "Not sent yet")]
    [InlineData("No longer in Old Box", "No longer downloading")]
    [InlineData("Download failed, Anime Sonarr will try another release (3 episodes)", "Retrying download (3 episodes)")]
    [InlineData("Downloading 40%", "Downloading 40%")]
    public void Neutral_ProgressText_LeavesOutServersNames(string text, string expected)
        => Assert.Equal(expected, RequestsController.Neutral(text));

    /// <summary>
    /// A request does not point at a library item the viewer cannot see, nor its artwork, but keeps outside artwork.
    /// True: a title in a library hidden from the viewer is never revealed through a request.
    /// False: a request would link or show the poster of an item the viewer may not see.
    /// </summary>
    [Fact]
    public void HideUnseen_ItemTheViewerCannotSee_IsLeftOut()
    {
        const string Hidden = "0123456789abcdef0123456789abcdef";
        const string Seen = "fedcba9876543210fedcba9876543210";
        bool CanSee(Guid id) => id != Guid.Parse(Hidden);

        var shown = RequestsController.HideUnseen(new MediaRequest
        {
            Title = "Dune",
            LibraryItemId = Guid.Parse(Hidden),
            PosterUrl = "../Items/" + Hidden + "/Images/Primary?fillHeight=480&quality=90",
            BackdropUrl = "../Items/" + Hidden + "/Images/Backdrop?fillWidth=1280&quality=80"
        }, CanSee);
        var visible = RequestsController.HideUnseen(new MediaRequest
        {
            LibraryItemId = Guid.Parse(Seen),
            PosterUrl = "../Items/" + Seen + "/Images/Primary?fillHeight=480&quality=90",
            BackdropUrl = "https://image.tmdb.org/t/p/w1280/b.jpg"
        }, CanSee);

        Assert.Null(shown.LibraryItemId);
        Assert.Null(shown.PosterUrl);
        Assert.Null(shown.BackdropUrl);
        Assert.Equal("Dune", shown.Title);
        Assert.Equal(Guid.Parse(Seen), visible.LibraryItemId);
        Assert.NotNull(visible.PosterUrl);
        Assert.Equal("https://image.tmdb.org/t/p/w1280/b.jpg", visible.BackdropUrl);
    }

    /// <summary>
    /// Details say blocked by the same rules a submit uses, the reason only to managers, and unblockable only when listed.
    /// True: the details page and a submit agree, and a manager sees why and can unblock a listed title.
    /// False: the page would offer a request that is refused, or tell requesters the blocklist.
    /// </summary>
    [Fact]
    public void WithBlocklist_Details_UseTheSubmitRules()
    {
        var config = new RequestSettings
        {
            BlockedGenres = { "Horror" },
            BlockedTitles = { new BlockedTitle { Type = RequestType.Movie, Title = "Cats", Tmdb = "536869" } }
        };
        var lookup = new DetailsLookup { Type = RequestType.Movie, Title = "It", ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "346364" } };
        var listed = new DetailsLookup { Type = RequestType.Movie, Title = "Cats", ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "536869" } };
        var details = new TitleDetails { Genres = { "Horror" } };

        var forViewer = RequestsController.WithBlocklist(details, lookup, config, false);
        var forAdmin = RequestsController.WithBlocklist(details, lookup, config, true);
        var free = RequestsController.WithBlocklist(new TitleDetails { Genres = { "Drama" } }, lookup, config, true);

        Assert.True(forViewer.Blocked);
        Assert.Null(forViewer.TitleBlocked);
        Assert.Null(forViewer.BlockedReason);
        Assert.True(forAdmin.Blocked);
        Assert.False(forAdmin.TitleBlocked);
        Assert.Equal("The Horror genre is blocked", forAdmin.BlockedReason);
        Assert.True(RequestsController.WithBlocklist(new TitleDetails(), listed, config, true).TitleBlocked);
        Assert.False(free.Blocked);
        Assert.Null(details.Blocked);
    }

    // ------------------------------------------------------------------ settings

    /// <summary>
    /// Redacting keeps every setting but the keys, which become the kept sentinel, and leaves the original as it was.
    /// True: Settings shows every setting and never a key.
    /// False: a key would reach the page, or a setting would be lost from what it shows.
    /// </summary>
    [Fact]
    public void Redact_Settings_KeepsEverySettingButTheKeys()
    {
        var config = new RequestSettings();
        var n = 0;
        foreach (var property in typeof(RequestSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite))
        {
            n++;
            if (property.PropertyType == typeof(string))
            {
                property.SetValue(config, "value" + n);
            }
            else if (property.PropertyType == typeof(int))
            {
                property.SetValue(config, 1000 + n);
            }
            else if (property.PropertyType == typeof(bool))
            {
                property.SetValue(config, !(bool)property.GetValue(config)!);
            }
        }

        config.ArrServers.Add(new ArrServer { Id = "r1", Url = "http://radarr", ApiKey = "enc:v1:key", Profiles = { new ArrProfileOption { Id = "hd", QualityProfileId = 1, RootFolder = "/movies" } } });
        config.BlockedTitles.Add(new BlockedTitle { Title = "Cats", Tmdb = "536869" });
        config.AutoApproveRatings.Add("G");
        config.TmdbApiKey = "enc:v1:tmdb";

        var redacted = RequestsController.Redact(config);

        Assert.Equal(SecretProtector.KeptSentinel, Assert.Single(redacted.ArrServers).ApiKey);
        Assert.Equal(SecretProtector.KeptSentinel, redacted.TmdbApiKey);
        Assert.Equal("enc:v1:key", config.ArrServers[0].ApiKey);
        Assert.Equal("enc:v1:tmdb", config.TmdbApiKey);
        redacted.ArrServers[0].ApiKey = config.ArrServers[0].ApiKey;
        redacted.TmdbApiKey = config.TmdbApiKey;
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(config), System.Text.Json.JsonSerializer.Serialize(redacted));
    }

    /// <summary>
    /// A stored key is tested only at the address it was saved for, and a moved server needs its key again to save.
    /// True: a key never follows a server to a new address.
    /// False: someone could point a server at their own address and receive the stored key.
    /// </summary>
    [Fact]
    public void KeyToTest_StoredKey_GoesOnlyToItsAddress()
    {
        var stored = new RequestSettings
        {
            ArrServers = { new ArrServer { Id = "r1", Type = RequestType.Movie, Name = "Radarr", Url = "http://radarr:7878/", ApiKey = "enc:v1:key", Profiles = { new ArrProfileOption { Id = "hd", QualityProfileId = 1, RootFolder = "/movies" } } } }
        };
        var keep = new RequestSettings { ArrServers = { new ArrServer { Id = "r1", Type = RequestType.Movie, Url = "http://radarr:7878", ApiKey = SecretProtector.KeptSentinel, Profiles = { new ArrProfileOption { Id = "hd", QualityProfileId = 1, RootFolder = "/movies" } } } } };

        var same = RequestsController.KeyToTest(new ArrTestRequest { ServerId = "r1", Url = "http://RADARR:7878", ApiKey = SecretProtector.KeptSentinel }, RequestType.Movie, stored);
        var moved = RequestsController.KeyToTest(new ArrTestRequest { ServerId = "r1", Url = "http://evil.example.com", ApiKey = SecretProtector.KeptSentinel }, RequestType.Movie, stored);

        Assert.Equal(("enc:v1:key", true, (string?)null), same);
        Assert.False(moved.Stored);
        Assert.NotNull(moved.Error);
        Assert.NotNull(RequestsController.KeyToTest(new ArrTestRequest { ServerId = "r1", Url = "http://radarr:7878", ApiKey = SecretProtector.KeptSentinel }, RequestType.Series, stored).Error);
        Assert.Equal(("typed", false, (string?)null), RequestsController.KeyToTest(new ArrTestRequest { ServerId = "r1", Url = "http://elsewhere", ApiKey = "typed" }, RequestType.Movie, stored));
        Assert.Null(RequestsController.ArrServersError(keep, stored));
        keep.ArrServers[0].Url = "http://evil.example.com";
        Assert.Equal("Enter Radarr's API key again for its new address.", RequestsController.ArrServersError(keep, stored));
    }

    /// <summary>
    /// A server's external address must be an http or https address.
    /// True: the queue's link to a server opens a web page.
    /// False: a link could be a bare host or another scheme.
    /// </summary>
    [Fact]
    public void ArrServersError_ExternalUrl_MustBeAWebAddress()
    {
        var config = new RequestSettings { ArrServers = { new ArrServer { Id = "r1", Name = "Radarr", Type = RequestType.Movie, Url = "http://r1", ApiKey = "k", Profiles = { new ArrProfileOption { Id = "r1p", QualityProfileId = 1, RootFolder = "/media" } } } } };

        Assert.Null(RequestsController.ArrServersError(config, new RequestSettings()));
        config.ArrServers[0].ExternalUrl = "https://radarr.example.com/";
        Assert.Null(RequestsController.ArrServersError(config, new RequestSettings()));
        config.ArrServers[0].ExternalUrl = "radarr.example.com";
        Assert.Equal("Radarr's external URL must start with http:// or https://.", RequestsController.ArrServersError(config, new RequestSettings()));
        config.ArrServers[0].ExternalUrl = "ftp://radarr.example.com";
        Assert.NotNull(RequestsController.ArrServersError(config, new RequestSettings()));
    }

    // ------------------------------------------------------------------ collections

    /// <summary>
    /// A collection's movies are requested a few at a time, those in the library skipped, and counted in order.
    /// True: requesting a collection reports how many were made, skipped, and refused, with the first refusal.
    /// False: a collection would flood the server, or report the wrong counts.
    /// </summary>
    [Fact]
    public async Task RequestEach_Collection_RequestsAFewAtATime()
    {
        var movies = Enumerable.Range(0, 14).Select(i => new SearchResult { Title = "Movie " + i, LibraryItemId = i % 3 == 0 ? Guid.NewGuid() : null }).ToList();
        var running = 0;
        var most = 0;
        var made = new System.Collections.Concurrent.ConcurrentBag<string>();
        async Task<(MediaRequest? Request, string? Error)> Submit(SearchResult movie)
        {
            var now = Interlocked.Increment(ref running);
            lock (made)
            {
                most = Math.Max(most, now);
            }

            try
            {
                await Task.Delay(movie.Title == "Movie 5" ? 150 : 20);
                if (movie.Title is "Movie 5" or "Movie 11")
                {
                    return (null, movie.Title + " refused");
                }

                made.Add(movie.Title);
                return (new MediaRequest { Title = movie.Title }, null);
            }
            finally
            {
                Interlocked.Decrement(ref running);
            }
        }

        var result = await RequestsController.RequestEach(movies, Submit, 4, CancellationToken.None);
        var mostAtOnce = most;
        var madeFirst = made.Count;
        most = 0;
        var alone = await RequestsController.RequestEach(movies, Submit, 1, CancellationToken.None);

        Assert.Equal(5, result.Skipped);
        Assert.Equal(7, result.Done);
        Assert.Equal(2, result.Failed);
        Assert.Equal("Movie 5 refused", result.Error);
        Assert.Equal(7, madeFirst);
        Assert.InRange(mostAtOnce, 2, 4);
        Assert.Equal((result.Done, result.Skipped, result.Failed, result.Error), (alone.Done, alone.Skipped, alone.Failed, alone.Error));
        Assert.Equal(1, most);
    }

    // ------------------------------------------------------------------ web client assets

    /// <summary>
    /// The page imports the web client's stylesheets: index.html's links in order, the site and layout chunks found by
    /// what they hold, and the screens it stands in for.
    /// True: the page looks like the web client, with its phone scaling, and not like its book player.
    /// False: the page would take the wrong chunk's root size, or miss a screen's styles.
    /// </summary>
    [Fact]
    public void FindWebClientStyles_WebClient_ImportsItsStylesheetsInOrder()
    {
        var web = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(web, "index.html"), "<link href=\"1133.ab12.css\" rel=\"stylesheet\"><link href=\"main.jellyfin.abc.css\" rel=\"stylesheet\">");
            File.WriteAllText(Path.Combine(web, "66923.0db1.css"), "html{font-size:82%!important}");
            File.WriteAllText(Path.Combine(web, "25348.1004.css"), "html{font-size:93%}h2{font-weight:400}.layout-mobile{font-size:90%}");
            File.WriteAllText(Path.Combine(web, "78701.1521.css"), ".layout-mobile .itemBackdrop:before{content:\"\"}");
            File.WriteAllText(Path.Combine(web, "search.d874.css"), ".searchFields{}");
            File.WriteAllText(Path.Combine(web, "itemDetails.e5f6.css"), ".itemDetailPage{}");
            File.WriteAllText(Path.Combine(web, "list.a1b2.css"), ".itemsContainer{}");

            var css = RequestsController.FindWebClientStyles(web);

            Assert.Equal(
                new[] { "1133.ab12.css", "main.jellyfin.abc.css", "25348.1004.css", "78701.1521.css", "search.d874.css", "itemDetails.e5f6.css", "list.a1b2.css" }.Select(f => "@import url(\"../web/" + f + "\");"),
                css.Split('\n'));
            Assert.DoesNotContain("66923", css, StringComparison.Ordinal);
            Assert.Equal(string.Empty, RequestsController.FindWebClientStyles(Path.Combine(web, "missing")));
        }
        finally
        {
            Directory.Delete(web, true);
        }
    }

    /// <summary>
    /// Only the web client's Noto Sans faces are served, each loading its file from the web client, with the page's font stacks.
    /// True: the page uses the web client's own font without a copy of it.
    /// False: the page would load CJK faces it does not need, or fonts from a path that is not there.
    /// </summary>
    [Fact]
    public void FindFonts_WebClient_ServesOnlyItsNotoSansFaces()
    {
        var web = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(
                Path.Combine(web, "73560.9f8e.css"),
                "@font-face{font-family:Noto Sans;font-weight:400;src:url(assets/noto-sans-latin.woff2) format(\"woff2\")}"
                + "@font-face{font-family:Noto Sans JP;src:url(assets/noto-sans-jp.woff2)}"
                + "@font-face{font-family:Noto Sans;font-weight:700;src:url(data:font/woff2;base64,AAAA)}"
                + "html{font-family:Noto Sans,sans-serif}html[lang|=ja]{font-family:Noto Sans JP,sans-serif}");

            var css = RequestsController.FindFonts(web);
            var faces = Regex.Matches(css, @"@font-face\{[^}]*\}").Select(m => m.Value).ToList();
            var urls = Regex.Matches(css, @"url\(([^)]*)\)").Select(m => m.Groups[1].Value).ToList();

            Assert.Equal(2, faces.Count);
            Assert.All(faces, face => Assert.Contains("font-family:Noto Sans;", face, StringComparison.Ordinal));
            Assert.All(urls, url => Assert.True(url.StartsWith("../web/", StringComparison.Ordinal) || url.StartsWith("data:", StringComparison.Ordinal), url));
            Assert.Contains("html{font-family:", css, StringComparison.Ordinal);
            Assert.Contains("html[lang|=ja]{font-family:", css, StringComparison.Ordinal);
            Assert.Equal(string.Empty, RequestsController.FindFonts(null));
        }
        finally
        {
            Directory.Delete(web, true);
        }
    }

    // ------------------------------------------------------------------ the page

    /// <summary>
    /// The issue page is the request page under its own name, in the tab and as an installed app.
    /// True: /issues/ shows as Issues and installs as Jellyfin Issues.
    /// False: the issue page would read as Requests.
    /// </summary>
    [Fact]
    public void AsIssuesPage_RequestPage_IsNamedIssues()
    {
        var page = RequestsController.AsIssuesPage(PageHtml());
        var manifest = RequestsController.AsIssuesManifest(EmbeddedResources.Read("necto_requests_manifest.webmanifest"));

        Assert.Contains("<title>Issues</title>", page, StringComparison.Ordinal);
        Assert.Contains("<meta name=\"apple-mobile-web-app-title\" content=\"Issues\" />", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<title>Requests</title>", page, StringComparison.Ordinal);
        Assert.Contains("\"short_name\": \"Issues\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"name\": \"Jellyfin Issues\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"start_url\": \"./\"", manifest, StringComparison.Ordinal);
        Assert.Contains("var ISSUES = /\\/issues\\/?$/i.test(location.pathname);", PageHtml(), StringComparison.Ordinal);
    }

    /// <summary>
    /// While Issues are off, the issue page and every asset under it answer 404, and the request page is unchanged.
    /// True: a server with Issues off has no issue page.
    /// False: the issue page would load on a server that turned Issues off.
    /// </summary>
    [Theory]
    [InlineData("/issues/", false, true)]
    [InlineData("/issues/page.js", false, true)]
    [InlineData("/issues/", true, false)]
    [InlineData("/requests/", false, false)]
    public void OnActionExecuting_IssuePageWhileIssuesAreOff_IsNotFound(string path, bool on, bool gone)
    {
        H.Plugin.MutateConfiguration(c => { c.Issues.Enabled = on; return false; });
        var controller = Controller(path);
        var context = new ActionExecutingContext(
            new ActionContext(controller.HttpContext, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            controller);

        controller.OnActionExecuting(context);

        Assert.Equal(gone, context.Result is NotFoundResult);
    }

    /// <summary>
    /// The page's own stylesheet is served on its own in its place, before the server's custom CSS, and only the
    /// largest style block in the head is taken out.
    /// True: the page's styles are cached as a file, and the server's custom CSS still wins.
    /// False: the page would inline its styles each load, or lose the custom CSS's order.
    /// </summary>
    [Fact]
    public void SplitStyle_Page_ServesItsStylesheetOnItsOwn()
    {
        var page = PageHtml();
        var head = page[..page.IndexOf("</head>", StringComparison.Ordinal)];
        var open = head.IndexOf("<style>", StringComparison.Ordinal);
        var own = head[(open + "<style>".Length)..head.IndexOf("</style>", open, StringComparison.Ordinal)];

        var (withoutScript, script) = RequestsController.SplitScript(RequestsController.BuildPage(page, "v1", null), "v1");
        var (html, style) = RequestsController.SplitStyle(withoutScript, "v1");
        var builtHead = html[..html.IndexOf("</head>", StringComparison.Ordinal)];

        Assert.Equal(own, style);
        Assert.DoesNotContain("<style>", builtHead, StringComparison.Ordinal);
        Assert.Contains("<link rel=\"stylesheet\" href=\"page.css?v=v1\" />", builtHead, StringComparison.Ordinal);
        Assert.True(builtHead.IndexOf("page.css?v=v1", StringComparison.Ordinal) < builtHead.IndexOf("id=\"brandingCss\"", StringComparison.Ordinal));
        Assert.Contains("function begin()", script, StringComparison.Ordinal);
        Assert.Contains("<script src=\"page.js?v=v1\"></script>", html, StringComparison.Ordinal);
        Assert.Equal(head.Split("<script>").Length, builtHead.Split("<script>").Length);
    }

    /// <summary>
    /// Only the largest style block in the head is taken out, and a page without a head is left as it is.
    /// True: small inline styles the page needs at once stay inline.
    /// False: the wrong block would be served apart, or a body style moved.
    /// </summary>
    [Fact]
    public void SplitStyle_SeveralBlocks_TakesTheLargestInTheHead()
    {
        const string Html = "<html><head><style>.a{}</style><style>.big{color:red}.more{}</style></head><body><style>.body{}</style></body></html>";

        var (html, style) = RequestsController.SplitStyle(Html, "v2");

        Assert.Equal(".big{color:red}.more{}", style);
        Assert.Equal("<html><head><style>.a{}</style><link rel=\"stylesheet\" href=\"page.css?v=v2\" /></head><body><style>.body{}</style></body></html>", html);
        Assert.Equal(("<p>no head</p>", string.Empty), RequestsController.SplitStyle("<p>no head</p>", "v2"));
    }

    /// <summary>
    /// The page is built with the web client's stylesheets linked and its own script served on its own, versioned.
    /// True: the page links the web client's styles in order and its script as page.js.
    /// False: the page would import webclient.css or carry its whole script inline.
    /// </summary>
    [Fact]
    public void BuildPage_Page_LinksItsStylesheetsAndSplitsItsScript()
    {
        var css = "@import url(\"../web/main.jellyfin.abc.css\");\n@import url(\"../web/12345.def.css\");";

        var (html, script) = RequestsController.SplitScript(RequestsController.BuildPage(PageHtml(), "v1", null, css), "v1");

        Assert.DoesNotContain("webclient.css", html, StringComparison.Ordinal);
        Assert.True(html.IndexOf("../web/main.jellyfin.abc.css", StringComparison.Ordinal) < html.IndexOf("../web/12345.def.css", StringComparison.Ordinal));
        Assert.Contains("<script src=\"page.js?v=v1\"></script>", html, StringComparison.Ordinal);
        Assert.Contains("function begin()", script, StringComparison.Ordinal);
        Assert.DoesNotContain("function begin()", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The page carries the web client's details template only when there is one, in its slot.
    /// True: the details page matches the installed web client, or keeps the page's own copy.
    /// False: an empty template would replace the page's own details page.
    /// </summary>
    [Fact]
    public void BuildPage_DetailsTemplate_GoesInItsSlot()
    {
        var page = "<head><link href=\"x.css?v=ASSET_VERSION\"></head><body>" + RequestsController.DetailsTemplateSlot + "<div id=\"view-details\"></div></body>";

        var with = RequestsController.BuildPage(page, "abc", "<div id=\"itemDetailPage\"></div>");
        var without = RequestsController.BuildPage(page, "abc", null);

        Assert.Contains("x.css?v=abc", with, StringComparison.Ordinal);
        Assert.Contains("<template id=\"webDetailsTemplate\"><div id=\"itemDetailPage\"></div></template><div id=\"view-details\">", with, StringComparison.Ordinal);
        Assert.DoesNotContain("<template", without, StringComparison.Ordinal);
        Assert.Equal(2, PageHtml().Split(RequestsController.DetailsTemplateSlot).Length);
    }

    /// <summary>
    /// The plugin's version is filled in.
    /// True: the page reports the version it was built with.
    /// False: the page would show PLUGIN_VERSION.
    /// </summary>
    [Fact]
    public void BuildPage_PluginVersion_IsFilledIn()
    {
        var built = RequestsController.BuildPage(PageHtml(), "v1", null);

        Assert.DoesNotContain("PLUGIN_VERSION", built, StringComparison.Ordinal);
        Assert.Contains("encodeURIComponent('" + RequestsController.PluginVersion + "')", built, StringComparison.Ordinal);
    }

    /// <summary>
    /// The page is served with its stylesheet linked, its scripts carrying the response's nonce, and a policy that allows its own styles.
    /// True: only the page's own scripts run on it.
    /// False: a script without the nonce could run, or the page's styles would be blocked.
    /// </summary>
    [Fact]
    public void Page_Served_LinksItsStylesheetAndNoncesItsScripts()
    {
        var controller = Controller("/requests/", Host(RequestHarness.ServerId));

        var html = Assert.IsType<ContentResult>(controller.Page()).Content!;
        var head = html[..html.IndexOf("</head>", StringComparison.Ordinal)];
        var css = Assert.IsType<FileContentResult>(Controller("/requests/page.css").PageStyles());

        Assert.Matches("<link rel=\"stylesheet\" href=\"page.css\\?v=[0-9a-f]{12}\" />", head);
        Assert.DoesNotContain("<style>", head, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script src=", html, StringComparison.Ordinal);
        Assert.Matches("<script nonce=\"[^\"]+\" src=\"page.js\\?v=[0-9a-f]{12}\"></script>", html);
        Assert.Contains("style-src 'self'", controller.Response.Headers.ContentSecurityPolicy.ToString(), StringComparison.Ordinal);
        Assert.StartsWith("text/css", css.ContentType, StringComparison.Ordinal);
        Assert.Contains("Layout only", System.Text.Encoding.UTF8.GetString(css.FileContents), StringComparison.Ordinal);
    }

    /// <summary>
    /// The page is served with the server's id, built for each server, and an id with markup is encoded.
    /// True: the page signs in against the server it came from.
    /// False: the page would carry another server's id, or let an id inject markup.
    /// </summary>
    [Fact]
    public void Page_Served_CarriesTheServersId()
    {
        var html = Assert.IsType<ContentResult>(Controller("/requests/", Host(RequestHarness.ServerId)).Page()).Content!;
        var other = Assert.IsType<ContentResult>(Controller("/requests/", Host("0123456789abcdef0123456789abcdef")).Page()).Content!;

        Assert.Contains("<meta name=\"requests-server-id\" content=\"" + RequestHarness.ServerId + "\" />", html, StringComparison.Ordinal);
        Assert.DoesNotContain(RequestsController.ServerIdSlot, html, StringComparison.Ordinal);
        Assert.Contains("content=\"0123456789abcdef0123456789abcdef\"", other, StringComparison.Ordinal);
        Assert.DoesNotContain(RequestHarness.ServerId, other, StringComparison.Ordinal);
        Assert.Equal("<meta content=\"REQUESTS_SERVER_ID\">", RequestsController.FillServerId("<meta content=\"REQUESTS_SERVER_ID\">", null));
        Assert.Equal("<meta content=\"&lt;x&gt;\">", RequestsController.FillServerId("<meta content=\"REQUESTS_SERVER_ID\">", "<x>"));
    }

    /// <summary>
    /// The fingerprint is worked out again only when what it is made from is replaced.
    /// True: the page's version costs nothing per load and changes with its files.
    /// False: every load would hash the page again, or a change would keep the old version.
    /// </summary>
    [Fact]
    public void AssetFingerprint_SameStrings_IsKept()
    {
        var fingerprint = new RequestsController.AssetFingerprint();
        var css = new string('a', 40000);
        var script = "script";

        var first = fingerprint.Of(css, script);
        var kept = fingerprint.Of(css, script);
        var again = fingerprint.Of(new string('a', 40000), script);

        Assert.Equal(RequestsController.AssetVersion(css, script), first);
        Assert.Same(first, kept);
        Assert.NotSame(first, again);
        Assert.Equal(first, again);
        Assert.NotEqual(first, fingerprint.Of(css + "b", script));
    }

    /// <summary>
    /// Scripts are served gzipped to browsers that take it, and plain to others, each varying by Accept-Encoding.
    /// True: the page's scripts load small, and caches keep the two apart.
    /// False: a browser refusing gzip would get it, or a cache would serve one to the other.
    /// </summary>
    [Fact]
    public void WebClientMarkup_AcceptsGzip_IsServedGzipped()
    {
        var webclient = EmbeddedResources.Read("necto_requests_webclient.js");
        var gzipped = Controller("/requests/webclient.js", acceptEncoding: "br, gzip;q=0.8");
        var plain = Controller("/requests/webclient.js", acceptEncoding: "gzip;q=0, br");
        var page = Controller("/requests/page.js", acceptEncoding: "gzip");

        var packed = Assert.IsType<FileContentResult>(gzipped.WebClientMarkup());
        var asIs = Assert.IsType<FileContentResult>(plain.WebClientMarkup());
        var pageScript = Assert.IsType<FileContentResult>(page.PageScript());
        var pagePlain = Assert.IsType<FileContentResult>(Controller("/requests/page.js").PageScript());

        Assert.Equal("gzip", gzipped.Response.Headers.ContentEncoding.ToString());
        Assert.Equal("Accept-Encoding", gzipped.Response.Headers.Vary.ToString());
        Assert.Equal(webclient, Gunzip(packed.FileContents));
        Assert.Empty(plain.Response.Headers.ContentEncoding.ToString());
        Assert.Equal("Accept-Encoding", plain.Response.Headers.Vary.ToString());
        Assert.Equal(webclient, System.Text.Encoding.UTF8.GetString(asIs.FileContents));
        Assert.Equal("gzip", page.Response.Headers.ContentEncoding.ToString());
        Assert.Equal("Accept-Encoding", page.Response.Headers.Vary.ToString());
        Assert.Contains("function begin()", Gunzip(pageScript.FileContents), StringComparison.Ordinal);
        Assert.Equal(Gunzip(pageScript.FileContents), System.Text.Encoding.UTF8.GetString(pagePlain.FileContents));
    }

    /// <summary>
    /// Packed assets unpack to what they were, smaller.
    /// True: gzipped scripts arrive intact, Unicode and all.
    /// False: a script would arrive damaged.
    /// </summary>
    [Fact]
    public void PackedAsset_Text_UnpacksToWhatItWas()
    {
        var text = "const s = `ünïcödé ${1}`; // ✓\n" + new string('x', 5000);

        var packed = RequestsController.PackedAsset.Of(text);

        Assert.Equal(text, System.Text.Encoding.UTF8.GetString(packed.Bytes));
        Assert.Equal(text, Gunzip(packed.Gzipped));
        Assert.True(packed.Gzipped.Length < packed.Bytes.Length);
    }

    /// <summary>
    /// The page's script leaves the web client's markup to its own file, building only the page's own classes.
    /// True: everything copied from the web client has one home to update.
    /// False: the web client's markup would spread through the page's script.
    /// </summary>
    [Fact]
    public void Page_Script_LeavesTheWebClientsMarkupToItsOwnFile()
    {
        var page = PageHtml();
        var script = page[page.LastIndexOf("<script>", StringComparison.Ordinal)..];
        var own = new HashSet<string>(StringComparer.Ordinal) { "checkboxListTools", "formError", "issueHeader", "issueTitleRow", "pushSettings", "requestHeader", "requestInfo", "requestOverview", "requestTitle", "rq-homeRow", "rq-titleFull", "rq-titleShort" };

        var classes = Regex.Matches(script, "class=\"([^\"]*)\"").SelectMany(m => m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        Assert.All(classes, c => Assert.Contains(c, own));
        Assert.Contains("<script src=\"webclient.js?v=ASSET_VERSION\"></script>", page, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ sign in

    /// <summary>
    /// The password field is a password field, and the sign in form posts nowhere else.
    /// True: browsers treat the password as one and fill it in.
    /// False: the password would show, or the form could post to another address.
    /// </summary>
    [Fact]
    public void Page_SignInForm_PostsNowhereElse()
    {
        var page = PageHtml();
        var form = Regex.Match(page, "<form id=\"signinForm\"[^>]*>").Value;

        Assert.Contains("method=\"post\"", form, StringComparison.Ordinal);
        Assert.DoesNotContain("action=", form, StringComparison.Ordinal);
        Assert.Matches("<input id=\"signinPassword\"[^>]*type=\"password\"[^>]*autocomplete=\"current-password\"", page);
        Assert.Matches("<input id=\"signinUser\"[^>]*autocomplete=\"username\"", page);
        Assert.DoesNotContain("id=\"signinLink\"", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// Credentials go only to this server's own sign in, without the browser's cookies.
    /// True: the page signs in with Jellyfin and nowhere else.
    /// False: a password could be sent to another address.
    /// </summary>
    [Fact]
    public void Page_Credentials_GoOnlyToThisServer()
    {
        var script = Script();

        Assert.Contains("authenticate('/Users/AuthenticateByName'", script, StringComparison.Ordinal);
        Assert.Contains("authenticate('/Users/AuthenticateWithQuickConnect'", script, StringComparison.Ordinal);
        Assert.Contains("return fetch(prefix + path, {", script, StringComparison.Ordinal);
        Assert.Contains("credentials: 'omit'", script, StringComparison.Ordinal);
        Assert.Contains("e.preventDefault();", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// The sign in is shared with the web client and holds no password.
    /// True: signing in on one signs in on the other, and no password is stored.
    /// False: a password would be kept in the browser.
    /// </summary>
    [Fact]
    public void Page_SignIn_IsSharedAndHoldsNoPassword()
    {
        var script = Script();

        Assert.Contains("WEB_CREDENTIALS = 'jellyfin_credentials', WEB_DEVICE = '_deviceId2'", script, StringComparison.Ordinal);
        Assert.Contains("server.UserId = result.User.Id;", script, StringComparison.Ordinal);
        Assert.Contains("server.AccessToken = result.AccessToken;", script, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"server\.\w+ = [^;]*(Pw|password|signinPassword)", script);
        Assert.DoesNotContain("requests_credentials", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// Stored sign ins that cannot be read are never overwritten.
    /// True: the web client's saved servers survive a page that cannot read them.
    /// False: the page would wipe the web client's sign ins.
    /// </summary>
    [Fact]
    public void Page_UnreadableSignIns_AreNeverOverwritten()
    {
        var script = Script();

        Assert.Contains("throw new Error('Unreadable credentials')", script, StringComparison.Ordinal);
        Assert.Matches(@"function saveSignIn[\s\S]{0,120}var creds = readCredentials\(\);", script);
        Assert.Matches(@"function forgetSignIn[\s\S]{0,120}readCredentials\(\)", script);
    }

    /// <summary>
    /// Stored tokens are checked before they are sent.
    /// True: only a well formed token for this server is used.
    /// False: another server's token, or a crafted one, would be sent.
    /// </summary>
    [Fact]
    public void Page_StoredTokens_AreCheckedBeforeUse()
    {
        var script = Script();

        Assert.Contains("/^[A-Za-z0-9]{16,128}$/", script, StringComparison.Ordinal);
        Assert.Contains("validToken(s.AccessToken) && id && s.Id === id", script, StringComparison.Ordinal);
        Assert.Contains("result.ServerId !== serverId", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// Signing out, or being rejected, forgets the token and the home rows kept for the tab.
    /// True: nothing the viewer could see stays behind after they sign out.
    /// False: a signed out tab would keep the viewer's rows or token.
    /// </summary>
    [Fact]
    public void Page_SigningOut_ForgetsTheToken()
    {
        var script = Script();

        Assert.Contains("function signedOut() { forgetHome(); forgetSignIn(token);", script, StringComparison.Ordinal);
        Assert.Matches(@"call\('/Sessions/Logout', 'POST'\)[\s\S]{0,200}forgetHome\(\);", script);
        Assert.Contains("sessionStorage.removeItem(homeKey())", script, StringComparison.Ordinal);
        Assert.Contains("if (r.status === 401) { forgetSignIn(candidates[index]);", script, StringComparison.Ordinal);
        Assert.Matches(@"call\('/Sessions/Logout', 'POST'\)[\s\S]{0,200}forgetSignIn\(signingOut\);", script);
    }

    /// <summary>
    /// The device id is random and the client header encoded.
    /// True: each browser is its own device and its id cannot break the header.
    /// False: devices would share ids, or Math.random would make them guessable.
    /// </summary>
    [Fact]
    public void Page_DeviceId_IsRandomAndEncoded()
    {
        var script = Script();

        Assert.Contains("crypto.getRandomValues(bytes)", script, StringComparison.Ordinal);
        Assert.Contains("DeviceId=\"' + encodeURIComponent(deviceId())", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Math.random", script, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ installed app

    /// <summary>
    /// The manifest is installable and relative to the page.
    /// True: the page installs as an app under whatever address the server has.
    /// False: the app would open at the server's root, or not install.
    /// </summary>
    [Fact]
    public void Manifest_IsInstallableAndRelative()
    {
        var manifest = JsonNode.Parse(EmbeddedResources.Read("necto_requests_manifest.webmanifest"))!.AsObject();
        var icons = manifest["icons"]!.AsArray().Select(i => i!.AsObject()).ToList();

        Assert.Equal("./", (string?)manifest["start_url"]);
        Assert.Equal("./", (string?)manifest["scope"]);
        Assert.Equal("standalone", (string?)manifest["display"]);
        Assert.False(string.IsNullOrWhiteSpace((string?)manifest["name"]));
        Assert.False(string.IsNullOrWhiteSpace((string?)manifest["short_name"]));
        Assert.Contains(icons, i => (string?)i["sizes"] == "512x512");
        Assert.All(icons, i => Assert.False(((string?)i["src"])!.StartsWith('/')));
    }

    /// <summary>
    /// The manifest is served as a manifest.
    /// True: browsers read it as the app's manifest.
    /// False: the browser would ignore it.
    /// </summary>
    [Fact]
    public void WebManifest_IsServedAsAManifest()
    {
        var result = Assert.IsType<ContentResult>(Controller("/requests/manifest.webmanifest").WebManifest());

        Assert.Equal("application/manifest+json", result.ContentType);
        Assert.NotNull(JsonNode.Parse(result.Content!));
    }

    /// <summary>
    /// The service worker is served uncached, leaves signed in calls alone, keeps navigation preload off, and loads Stolon's push script.
    /// True: the app never caches a user's data, opens on a cold start, and shows browser notifications.
    /// False: the worker could cache private answers, open blank, or show no notifications.
    /// </summary>
    [Fact]
    public void ServiceWorker_IsUncachedAndLeavesSignedInCallsAlone()
    {
        var controller = Controller("/requests/sw.js");
        var push = Controller("/requests/push-worker.js");

        var result = Assert.IsType<ContentResult>(controller.ServiceWorker());
        var pushed = Assert.IsType<ContentResult>(push.PushWorker());

        Assert.StartsWith("text/javascript", result.ContentType, StringComparison.Ordinal);
        Assert.Equal("no-cache", controller.Response.Headers.CacheControl.ToString());
        Assert.Contains("request.headers.has('Authorization')", result.Content, StringComparison.Ordinal);
        Assert.Contains("request.method !== 'GET'", result.Content, StringComparison.Ordinal);
        Assert.Contains("navigationPreload.disable()", result.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("preloadResponse", result.Content, StringComparison.Ordinal);
        Assert.Contains("importScripts('push-worker.js');", result.Content, StringComparison.Ordinal);
        Assert.StartsWith("text/javascript", pushed.ContentType, StringComparison.Ordinal);
        Assert.Equal("no-cache", push.Response.Headers.CacheControl.ToString());
        Assert.Equal("nosniff", push.Response.Headers["X-Content-Type-Options"].ToString());
    }

    /// <summary>
    /// The page loads Stolon's page script, served beside it.
    /// True: the page's headings take their casing from Stolon.
    /// False: the page would load a script the server does not serve.
    /// </summary>
    [Fact]
    public void StolonPage_IsServedBesideThePage()
    {
        var controller = Controller("/requests/stolon-page.js");

        var script = Assert.IsType<ContentResult>(controller.StolonPage());

        Assert.Contains("<script src=\"stolon-page.js?v=ASSET_VERSION\"></script>", PageHtml(), StringComparison.Ordinal);
        Assert.Equal(StolonPageScript.Script, script.Content);
        Assert.Equal("nosniff", controller.Response.Headers["X-Content-Type-Options"].ToString());
    }

    /// <summary>
    /// The page links the manifest and registers the service worker.
    /// True: the page offers to install itself.
    /// False: the page would never install as an app.
    /// </summary>
    [Fact]
    public void Page_Head_LinksTheManifestAndRegistersTheWorker()
    {
        var page = PageHtml();
        var head = page[..page.IndexOf("</head>", StringComparison.Ordinal)];

        Assert.Contains("<link rel=\"manifest\" href=\"manifest.webmanifest?v=ASSET_VERSION\" />", head, StringComparison.Ordinal);
        Assert.Contains("<meta name=\"theme-color\"", head, StringComparison.Ordinal);
        Assert.Contains("<link rel=\"apple-touch-icon\"", head, StringComparison.Ordinal);
        Assert.Contains("navigator.serviceWorker.register('sw.js')", Script(), StringComparison.Ordinal);
    }

    /// <summary>
    /// In a browser, the page turns notifications on through the worker every user page shares, at the server's root,
    /// and moves a subscription made through its own worker onto it. The installed app keeps its own worker.
    /// True: one subscription serves /requests, /issues, and /invites in a browser, and an installed app still notifies.
    /// False: a browser could hold two subscriptions and show each notification twice.
    /// </summary>
    [Fact]
    public void Page_BrowserNotifications_UseTheSharedWorker()
    {
        var script = Script();

        Assert.Contains("navigator.serviceWorker.register(prefix + '/Notifications/Push/Worker.js', { scope: prefix + '/' })", script, StringComparison.Ordinal);
        Assert.Contains("installed() ? appWorker()", script, StringComparison.Ordinal);
        Assert.Contains("dropAppSubscription()", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every icon the app names is served, one of them maskable.
    /// True: the installed app shows its icon everywhere.
    /// False: an icon the manifest names would be missing.
    /// </summary>
    [Fact]
    public void AppIcon_EveryIconTheManifestNames_IsServed()
    {
        var manifest = JsonNode.Parse(EmbeddedResources.Read("necto_requests_manifest.webmanifest"))!.AsObject();
        var named = manifest["icons"]!.AsArray().Select(i => ((string?)i!["src"])!["icons/".Length..]).Append("requests-180.png");

        Assert.All(named, name =>
        {
            var result = Assert.IsType<FileStreamResult>(Controller("/requests/icons/" + name).AppIcon(name));
            Assert.Equal("image/png", result.ContentType);
            Assert.True(result.FileStream.Length > 0);
        });
        Assert.Contains(manifest["icons"]!.AsArray(), i => (string?)i!["purpose"] == "maskable");
    }

    /// <summary>
    /// Only the app's own icons are served.
    /// True: the icon route cannot read other files.
    /// False: a crafted name could read the plugin's other resources.
    /// </summary>
    [Theory]
    [InlineData("../necto_requests_page.html")]
    [InlineData("other.png")]
    public void AppIcon_OtherName_IsNotFound(string name)
        => Assert.IsType<NotFoundResult>(Controller().AppIcon(name));

    /// <summary>
    /// The page's policy allows the service worker and the manifest.
    /// True: the app installs under the page's strict policy.
    /// False: the policy would block the worker or the manifest.
    /// </summary>
    [Fact]
    public void Page_Policy_AllowsTheWorkerAndManifest()
    {
        var controller = Controller();

        Assert.IsType<ContentResult>(controller.Page());

        var policy = controller.Response.Headers.ContentSecurityPolicy.ToString();
        Assert.Contains("worker-src 'self'", policy, StringComparison.Ordinal);
        Assert.Contains("manifest-src 'self'", policy, StringComparison.Ordinal);
    }

    private static string PageHtml() => EmbeddedResources.Read("necto_requests_page.html");

    private static string Script() => RequestsController.SplitScript(PageHtml(), "v1").Script;

    private static RequestsController Controller(string path = "/requests/", IServerApplicationHost? host = null, string? acceptEncoding = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        if (acceptEncoding is not null)
        {
            context.Request.Headers.AcceptEncoding = acceptEncoding;
        }

        return new RequestsController(null!, null!, null!, null!, null!, null!, InterfaceStub.Of<IServerApplicationPaths>(), null!, null!, host)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    private static IServerApplicationHost Host(string id) => InterfaceStub.Of<IServerApplicationHost>(("get_SystemId", _ => id));

    private static string Gunzip(byte[] gzipped)
    {
        using var input = new MemoryStream(gzipped);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // Runs the controller's filter for a route carrying the attributes given, as the caller.
    private IActionResult? Filter(User caller, params object[] metadata)
    {
        var controller = H.Api(caller);
        controller.HttpContext.Request.Path = "/Requests/Anything";
        var descriptor = new ControllerActionDescriptor { EndpointMetadata = metadata.ToList() };
        var context = new ActionExecutingContext(
            new ActionContext(controller.HttpContext, new RouteData(), descriptor),
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            controller);

        controller.OnActionExecuting(context);
        return context.Result;
    }

    private async Task<MediaRequest> Submit(User user, int tmdb)
    {
        var (made, error) = await H.Requests.SubmitAsync(
            H.Access(user),
            new NewMediaRequest { Type = RequestType.Movie, Title = "Movie " + tmdb, Year = 2020, ProviderIds = new() { ["Tmdb"] = tmdb.ToString(System.Globalization.CultureInfo.InvariantCulture) } },
            CancellationToken.None);
        Assert.Null(error);
        return made!;
    }

    private static RequestsQueryResult<RequestItemDto> Page(ActionResult<RequestsQueryResult<RequestItemDto>> result)
        => Assert.IsType<RequestsQueryResult<RequestItemDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
}
