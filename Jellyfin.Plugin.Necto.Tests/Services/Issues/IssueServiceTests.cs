using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;
using Jellyfin.Plugin.Necto.Services.Notifications;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Issues;

/// <summary>
/// The issue service keeps a report with a snapshot of its item, lets others join it and reporters change or withdraw
/// their part, lets managers decide it, marks it when its file changes, and sends each report and decision to
/// Notifications. It reads the loaded plugin's settings, so it runs on its own.
/// </summary>
[Collection(ServicesCollection.Name)]
public sealed class IssueServiceTests : IDisposable
{
    private readonly RequestHarness _h = new();
    private readonly User _viewer;
    private readonly User _other;
    private readonly User _admin;
    private readonly Movie _movie;

    public IssueServiceTests()
    {
        _h.Plugin.MutateConfiguration(c => { c.Issues.Enabled = true; return false; });
        _viewer = _h.AddUser("viewer");
        _other = _h.AddUser("other");
        _admin = _h.AddUser("admin", admin: true);
        _movie = Movie("The Lighthouse Keeper", 2019);
    }

    public void Dispose() => _h.Dispose();

    private Movie Movie(string name, int year)
    {
        var movie = new Movie
        {
            Id = Guid.NewGuid(),
            Name = name,
            ProductionYear = year,
            Path = "/media/movies/" + name + "/" + name + ".mkv",
            Size = 1000,
            DateModified = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "603", ["Imdb"] = "tt0133093" }
        };
        _h.Library.Add(movie);
        return movie;
    }

    private Issue Report(User user, IssueType type = IssueType.Audio, string? note = "The sound drops out.", BaseItem? item = null)
    {
        var (issue, error) = _h.Issues.Report(_h.Access(user), new IssueSubmit { ItemId = (item ?? _movie).Id, Type = type, Note = note });
        Assert.Null(error);
        return issue!;
    }

    /// <summary>
    /// A report keeps a snapshot of its item and its file, with the reporter's type and note.
    /// True: managers see what was reported on as it was, even after the item changes.
    /// False: a report would lose its item's details, its file, or its reporter.
    /// </summary>
    [Fact]
    public void Report_Item_KeepsASnapshotOfItAndItsFile()
    {
        var issue = Report(_viewer);

        var stored = _h.Db.GetIssue(issue.Id)!;
        Assert.Equal(IssueState.Reported, stored.State);
        Assert.Equal("Movie", stored.ItemType);
        Assert.Equal("The Lighthouse Keeper", stored.Title);
        Assert.Equal(2019, stored.Year);
        Assert.Equal(_movie.Path, stored.Path);
        Assert.Equal(1000, stored.FileSize);
        Assert.Equal("603", stored.ProviderIds["Tmdb"]);
        Assert.Equal(_viewer.Id, Assert.Single(stored.Reporters).UserId);
        Assert.Equal(IssueType.Audio, stored.Reporters[0].Type);
        var note = Assert.Single(stored.Notes);
        Assert.Equal(("viewer", "The sound drops out.", false), (note.User, note.Text, note.Admin));
        Assert.Equal("The Lighthouse Keeper (2019)", IssueService.Describe(stored, null));
    }

    /// <summary>
    /// Activity entries name who did what on which title, without the note.
    /// True: the log reads Viewer reported a video issue on The Lighthouse Keeper (2019).
    /// False: the log would lose who acted, or show a private note.
    /// </summary>
    [Fact]
    public void Decide_Activity_NamesWhoAndTheTitleWithoutTheNote()
    {
        var issue = Report(_viewer, IssueType.Video);
        _h.Issues.Join(_h.Access(_other), issue.Id, new IssueSubmit { Type = IssueType.Audio, Note = "Freezes." });
        _h.Issues.Decide(_h.Access(_admin), issue.Id, IssueAction.Confirm, "Seen it.");
        _h.Issues.Decide(_h.Access(_admin), issue.Id, IssueAction.Resolve, "Replaced the file.");

        Assert.Equal(
            new[]
            {
                ("Issue reported", "Viewer reported a video issue on The Lighthouse Keeper (2019)"),
                ("Issue reported", "Other also reported an audio issue on The Lighthouse Keeper (2019)"),
                ("Issue confirmed", "Admin confirmed the issue on The Lighthouse Keeper (2019)"),
                ("Issue resolved", "Admin resolved the issue on The Lighthouse Keeper (2019)")
            },
            _h.Activity.Entries.Select(e => (e.Name, e.ShortOverview ?? string.Empty)).ToArray());
        Assert.Equal(_viewer.Id, _h.Activity.Entries.First().UserId);
    }

    /// <summary>
    /// An episode reads as its show and number, and keeps its show's ids.
    /// True: managers and Notifications see which episode of which show.
    /// False: an episode would read as a bare episode name, or lose its show.
    /// </summary>
    [Fact]
    public void Report_Episode_ReadsAsItsShowAndNumber()
    {
        var series = new Series { Id = Guid.NewGuid(), Name = "Tidewater Station", ProviderIds = new Dictionary<string, string> { ["Tvdb"] = "81189" } };
        var episode = new Episode { Id = Guid.NewGuid(), Name = "Pilot", SeriesName = "Tidewater Station", SeriesId = series.Id, ParentIndexNumber = 1, IndexNumber = 2, Path = "/media/shows/t/s01e02.mkv" };
        _h.Library.Add(series);
        _h.Library.Add(episode);

        var issue = Report(_viewer, item: episode);

        Assert.Equal("Tidewater Station · S1E2", IssueService.Describe(issue, null));
        Assert.Equal("Tidewater Station - S01E02 · Pilot", IssueService.NotifyTitle(issue, null));
        Assert.Equal("Tidewater Station - S01E02 · Pilot", RequestNotifier.IssueValues(issue)["title"]);
        Assert.Equal("81189", issue.SeriesProviderIds!["Tvdb"]);
        Assert.Equal(series.Id, issue.SeriesId);
    }

    /// <summary>
    /// Only something that plays on its own takes an issue, never a show, a season, or a folder.
    /// True: an issue is always about one file.
    /// False: a report on a show would have no file to look at.
    /// </summary>
    [Fact]
    public void Report_Folder_IsRefused()
    {
        var series = new Series { Id = Guid.NewGuid(), Name = "Tidewater Station" };
        _h.Library.Add(series);

        var (issue, error) = _h.Issues.Report(_h.Access(_viewer), new IssueSubmit { ItemId = series.Id, Type = IssueType.Video });

        Assert.Null(issue);
        Assert.Equal("That item is not in the library.", error);
        Assert.True(IssueService.IsPlayable(_movie));
        Assert.False(IssueService.IsPlayable(series));
    }

    /// <summary>
    /// An issue of type Other needs a note.
    /// True: every Other report says what is wrong.
    /// False: a manager would get a report that says nothing.
    /// </summary>
    [Fact]
    public void Report_OtherWithoutANote_IsRefused()
    {
        var (issue, error) = _h.Issues.Report(_h.Access(_viewer), new IssueSubmit { ItemId = _movie.Id, Type = IssueType.Other, Note = "  " });

        Assert.Null(issue);
        Assert.Equal("Add a note saying what is wrong.", error);
    }

    /// <summary>
    /// An item with an open issue takes no second report, its reporter changing it and others joining instead.
    /// True: one item has one open issue.
    /// False: the same problem would be reported many times.
    /// </summary>
    [Fact]
    public void Report_OpenIssue_TakesNoSecondReport()
    {
        Report(_viewer);

        Assert.Equal("You already reported an issue with this. Change it instead.", _h.Issues.Report(_h.Access(_viewer), new IssueSubmit { ItemId = _movie.Id, Type = IssueType.Video }).Error);
        Assert.Equal("Someone already reported an issue with this. Join it instead.", _h.Issues.Report(_h.Access(_other), new IssueSubmit { ItemId = _movie.Id, Type = IssueType.Video }).Error);
    }

    /// <summary>
    /// Someone else joins an open issue with their own type and note, once.
    /// True: every viewer who sees the problem is counted on one issue.
    /// False: a join would lose their note, or count them twice.
    /// </summary>
    [Fact]
    public void Join_OpenIssue_AddsTheirTypeAndNote()
    {
        var issue = Report(_viewer);
        var before = _h.Issues.View(_h.Access(_other), _movie);
        Assert.Null(before.Mine);
        Assert.Equal(issue.Id, before.Open!.Id);

        var (joined, error) = _h.Issues.Join(_h.Access(_other), issue.Id, new IssueSubmit { Type = IssueType.Video, Note = "The picture freezes too." });

        Assert.Null(error);
        Assert.Equal(new[] { ("viewer", IssueType.Audio), ("other", IssueType.Video) }, joined!.Reporters.Select(r => (r.UserName, r.Type)));
        Assert.Equal(new[] { "The sound drops out.", "The picture freezes too." }, joined.Notes.Select(n => n.Text));
        var after = _h.Issues.View(_h.Access(_other), _movie);
        Assert.Equal(issue.Id, after.Mine!.Id);
        Assert.Null(after.Open);
        Assert.Equal("You already reported an issue with this. Change it instead.", _h.Issues.Join(_h.Access(_other), issue.Id, new IssueSubmit { Type = IssueType.Video }).Error);
    }

    /// <summary>
    /// A reporter changes only their own type and note, a blank note takes theirs out, and others cannot change it.
    /// True: a reporter can correct their part without touching anyone else's.
    /// False: a reporter could change another's report, or a stranger could edit the issue.
    /// </summary>
    [Fact]
    public void Edit_Reporter_ChangesOnlyTheirOwnPart()
    {
        var issue = Report(_viewer);
        _h.Issues.Join(_h.Access(_other), issue.Id, new IssueSubmit { Type = IssueType.Video, Note = "Freezes." });

        var (edited, _) = _h.Issues.Edit(_h.Access(_viewer), issue.Id, new IssueSubmit { Type = IssueType.Subtitles, Note = "Out of sync." });

        Assert.Equal(new[] { IssueType.Subtitles, IssueType.Video }, edited!.Reporters.Select(r => r.Type));
        Assert.Equal(new[] { "Out of sync.", "Freezes." }, edited.Notes.Select(n => n.Text));

        // A blank note takes theirs out.
        var (blank, _) = _h.Issues.Edit(_h.Access(_viewer), issue.Id, new IssueSubmit { Type = IssueType.Subtitles, Note = "" });
        Assert.Equal(new[] { "Freezes." }, blank!.Notes.Select(n => n.Text));

        // Someone who isn't on it can't change it.
        var stranger = _h.AddUser("stranger");
        Assert.Equal("That issue is gone.", _h.Issues.Edit(_h.Access(stranger), issue.Id, new IssueSubmit { Type = IssueType.Video }).Error);
    }

    /// <summary>
    /// The only reporter withdrawing an issue rescinds it, which frees the item for a new report.
    /// True: a report made by mistake can be taken back.
    /// False: a withdrawn issue would stay open, or block a new one.
    /// </summary>
    [Fact]
    public void Withdraw_OnlyReporter_RescindsIt()
    {
        var issue = Report(_viewer);

        var (gone, error) = _h.Issues.Withdraw(_h.Access(_viewer), issue.Id);

        Assert.Null(error);
        Assert.Equal(IssueState.Rescinded, gone!.State);
        Assert.Equal(IssueState.Rescinded, _h.Db.GetIssue(issue.Id)!.State);

        // It shows among the closed ones, and a new report can be made.
        var view = _h.Issues.View(_h.Access(_viewer), _movie);
        Assert.Null(view.Mine);
        Assert.Null(view.Open);
        Assert.Equal(IssueState.Rescinded, Assert.Single(view.Closed).State);
        Assert.Null(view.Refusal);
        Assert.NotEqual(issue.Id, Report(_viewer, IssueType.Video).Id);
    }

    /// <summary>
    /// A reporter withdrawing an issue others joined leaves it open for them, and the last one rescinds it.
    /// True: one person leaving never closes an issue others still have.
    /// False: others' reports would close with the first reporter's.
    /// </summary>
    [Fact]
    public void Withdraw_OthersJoined_LeavesItOpenForThem()
    {
        var issue = Report(_viewer);
        _h.Issues.Join(_h.Access(_other), issue.Id, new IssueSubmit { Type = IssueType.Video, Note = "Freezes." });

        var (left, _) = _h.Issues.Withdraw(_h.Access(_viewer), issue.Id);

        Assert.Equal(IssueState.Reported, left!.State);
        Assert.Equal(new[] { "other" }, left.Reporters.Select(r => r.UserName));
        Assert.Equal(new[] { "Freezes." }, left.Notes.Select(n => n.Text));

        // The last one left rescinds it.
        Assert.Equal(IssueState.Rescinded, _h.Issues.Withdraw(_h.Access(_other), issue.Id).Issue!.State);
        Assert.Equal("That issue is closed.", _h.Issues.Withdraw(_h.Access(_other), issue.Id).Error);
    }

    /// <summary>
    /// A manager confirms, resolves, declines, and reopens, each only from a state it fits, and never two open at once.
    /// True: the issue moves only along the steps the page offers.
    /// False: an issue could be resolved twice, or reopened beside another open one.
    /// </summary>
    [Fact]
    public void Decide_Manager_ConfirmsResolvesDeclinesAndReopens()
    {
        var issue = Report(_viewer);
        var admin = _h.Access(_admin);

        Assert.Equal(IssueState.Confirmed, _h.Issues.Decide(admin, issue.Id, IssueAction.Confirm, "Seen it.").Issue!.State);
        Assert.Equal("That issue is already confirmed.", _h.Issues.Decide(admin, issue.Id, IssueAction.Confirm, null).Error);
        Assert.Equal("That issue is already open.", _h.Issues.Decide(admin, issue.Id, IssueAction.Reopen, null).Error);

        var resolved = _h.Issues.Decide(admin, issue.Id, IssueAction.Resolve, "Replaced the file.").Issue!;
        Assert.Equal(IssueState.Resolved, resolved.State);
        Assert.Equal("admin", resolved.DecidedBy);
        Assert.Equal(new[] { ("viewer", false), ("admin", true), ("admin", true) }, resolved.Notes.Select(n => (n.User, n.Admin)));
        Assert.Equal("That issue is not open.", _h.Issues.Decide(admin, issue.Id, IssueAction.Decline, null).Error);

        Assert.Equal(IssueState.Reported, _h.Issues.Decide(admin, issue.Id, IssueAction.Reopen, null).Issue!.State);
        Assert.Equal(IssueState.Declined, _h.Issues.Decide(admin, issue.Id, IssueAction.Decline, null).Issue!.State);

        // A closed issue isn't opened again beside another open one for the same item.
        Report(_other, IssueType.Video);
        Assert.Equal("This item already has an open issue.", _h.Issues.Decide(admin, issue.Id, IssueAction.Reopen, null).Error);
    }

    /// <summary>
    /// The file changes when its path, size, or time does, and a missing item always differs.
    /// True: a replaced file is spotted however it was replaced.
    /// False: a replaced file would go unnoticed.
    /// </summary>
    [Theory]
    [InlineData("/media/movies/other.mkv", 1000L, 0, true)]
    [InlineData(null, 2000L, 0, true)]
    [InlineData(null, 1000L, 60, true)]
    [InlineData(null, 1000L, 0, false)]
    public void FileDiffers_PathSizeOrTime_IsAChange(string? path, long size, int minutes, bool changed)
    {
        var issue = IssueService.Snapshot(_movie);
        var now = new Movie { Id = _movie.Id, Name = _movie.Name, Path = path ?? _movie.Path, Size = size, DateModified = _movie.DateModified.AddMinutes(minutes) };

        Assert.Equal(changed, IssueService.FileDiffers(issue, now));
        Assert.True(IssueService.FileDiffers(issue, null));
    }

    /// <summary>
    /// An open issue is marked when its file changes, nothing is resolved by it, and reopening takes the file as it is.
    /// True: a manager sees which issues may be fixed by a new file.
    /// False: a rescan would close issues on its own, or mark them again and again.
    /// </summary>
    [Fact]
    public void CheckFiles_ChangedFile_MarksTheIssue()
    {
        var issue = Report(_viewer);
        Assert.Equal(0, _h.Issues.CheckFiles());

        _movie.Size = 5000;
        Assert.Equal(1, _h.Issues.CheckFiles(new[] { _movie.Id }));

        var marked = _h.Db.GetIssue(issue.Id)!;
        Assert.True(marked.FileChanged);
        Assert.Equal(IssueState.Reported, marked.State);
        Assert.Equal(0, _h.Issues.CheckFiles());

        // Reopened, it's about the file as it is now.
        _h.Issues.Decide(_h.Access(_admin), issue.Id, IssueAction.Resolve, null);
        var reopened = _h.Issues.Decide(_h.Access(_admin), issue.Id, IssueAction.Reopen, null).Issue!;
        Assert.False(reopened.FileChanged);
        Assert.Equal(5000, reopened.FileSize);
    }

    /// <summary>
    /// An issue whose item left the library is marked.
    /// True: a manager sees the item is gone.
    /// False: an issue would point at nothing without a sign.
    /// </summary>
    [Fact]
    public void CheckFiles_ItemGone_MarksTheIssue()
    {
        var issue = Report(_viewer);
        _h.Library.Remove(_movie);

        Assert.Equal(1, _h.Issues.CheckFiles());
        Assert.True(_h.Db.GetIssue(issue.Id)!.FileChanged);
    }

    /// <summary>
    /// Closed issues go after their retention, open ones stay, and 0 keeps them.
    /// True: the issue list does not grow without end, and nothing open is lost.
    /// False: an open issue would be cleaned away, or closed ones kept for ever.
    /// </summary>
    [Fact]
    public void Clean_Retention_RemovesClosedIssuesOnly()
    {
        var closed = Report(_viewer);
        _h.Issues.Withdraw(_h.Access(_viewer), closed.Id);
        var open = Report(_other, item: Movie("Paper Harbor", 2021));
        _h.Plugin.MutateConfiguration(c => { c.Issues.RetentionDays = 30; return false; });

        _h.Clock.Now = _h.Clock.Now.AddDays(29);
        Assert.Equal(0, _h.Issues.Clean());

        _h.Clock.Now = _h.Clock.Now.AddDays(2);
        Assert.Equal(1, _h.Issues.Clean());
        Assert.Null(_h.Db.GetIssue(closed.Id));
        Assert.NotNull(_h.Db.GetIssue(open.Id));

        // 0 keeps them.
        _h.Issues.Withdraw(_h.Access(_other), open.Id);
        _h.Plugin.MutateConfiguration(c => { c.Issues.RetentionDays = 0; return false; });
        _h.Clock.Now = _h.Clock.Now.AddDays(400);
        Assert.Equal(0, _h.Issues.Clean());
    }

    /// <summary>
    /// Reporting needs Issues on and a group that allows it, for administrators too.
    /// True: the page offers reporting only where both allow it.
    /// False: a user could report while Issues are off, or outside their groups.
    /// </summary>
    [Fact]
    public void Report_WithoutIssuesOrAGroup_IsRefused()
    {
        Assert.True(_h.Access(_viewer).ReportIssues);

        var barred = _h.AddUser("barred", permissions: p => p.Issues.MayReport = false);
        Assert.False(_h.Access(barred).ReportIssues);
        Assert.Equal("You cannot report issues.", _h.Issues.Report(_h.Access(barred), new IssueSubmit { ItemId = _movie.Id, Type = IssueType.Video }).Error);

        _h.Plugin.MutateConfiguration(c => { c.Issues.Enabled = false; return false; });
        Assert.False(_h.Access(_viewer).ReportIssues);
        Assert.False(_h.Access(_admin).ReportIssues);
    }

    /// <summary>
    /// Reports and joins count toward the group's issue limit, which rolls over, administrators have none, and Everyone's is 10.
    /// True: one user cannot flood the issue list.
    /// False: a limit would never free up, or would not hold.
    /// </summary>
    [Fact]
    public void Report_Limit_CountsReportsAndJoinsAndRollsOver()
    {
        var limited = _h.AddUser("limited", permissions: p => p.Issues.Limit = 2);
        Report(limited, item: Movie("One", 2020));
        var theirs = Report(_viewer, item: Movie("Two", 2020));
        Assert.Null(_h.Issues.Join(_h.Access(limited), theirs.Id, new IssueSubmit { Type = IssueType.Video }).Error);

        var third = Movie("Three", 2020);
        Assert.Equal("You have reported as many issues as you may for now. Try again later.", _h.Issues.Report(_h.Access(limited), new IssueSubmit { ItemId = third.Id, Type = IssueType.Video }).Error);
        Assert.NotNull(_h.Issues.View(_h.Access(limited), third).Refusal);

        _h.Clock.Now = _h.Clock.Now.AddDays(1).AddMinutes(1);
        Assert.Null(_h.Issues.Report(_h.Access(limited), new IssueSubmit { ItemId = third.Id, Type = IssueType.Video }).Error);

        // Administrators have no limit, and 10 a day is the default.
        Assert.Equal(10, _h.Access(_viewer).Permissions!.IssueQuotaLimit);
        Assert.Equal(0, _h.Access(_admin).Permissions!.IssueQuotaLimit);
    }

    /// <summary>
    /// A movie is found in Radarr and an episode in Sonarr by its show, and anything else nowhere.
    /// True: a manager's link opens the title's page in the right server.
    /// False: an episode would be looked for by its own ids, or a video in Radarr.
    /// </summary>
    [Fact]
    public void ArrTarget_MovieAndEpisode_AreFoundInTheirServer()
    {
        var movie = IssueService.ArrTarget(IssueService.Snapshot(_movie))!.Value;
        Assert.Equal(RequestType.Movie, movie.Type);
        Assert.Equal("603", movie.Ids["Tmdb"]);

        var episode = new Issue { ItemType = "Episode", ProviderIds = { ["Tvdb"] = "999" }, SeriesProviderIds = new() { ["Tvdb"] = "81189" } };
        var show = IssueService.ArrTarget(episode)!.Value;
        Assert.Equal(RequestType.Series, show.Type);
        Assert.Equal("81189", show.Ids["Tvdb"]);

        // Anything else, or an episode whose show has no ids, has no page there.
        Assert.Null(IssueService.ArrTarget(new Issue { ItemType = "Video", ProviderIds = { ["Tmdb"] = "1" } }));
        Assert.Null(IssueService.ArrTarget(new Issue { ItemType = "Episode", ProviderIds = { ["Tvdb"] = "999" } }));
    }

    /// <summary>
    /// The Radarr link is the movie's page in the Radarr that has it, and none until one does.
    /// True: a manager opens the movie in Radarr from the issue.
    /// False: the link would point at a Radarr that does not have the movie.
    /// </summary>
    [Fact]
    public async Task ArrLinkAsync_Movie_IsItsPageInTheRadarrThatHasIt()
    {
        _h.UseRadarr();
        var issue = Report(_viewer);
        Assert.Null(await _h.Issues.ArrLinkAsync(issue, CancellationToken.None));

        _h.RadarrMovies[603] = new JsonObject { ["id"] = 7, ["tmdbId"] = 603, ["title"] = "The Lighthouse Keeper" };
        var link = await _h.Issues.ArrLinkAsync(issue, CancellationToken.None);

        Assert.Equal(RequestHarness.RadarrUrl + "/movie/603", link!.Url);
        Assert.Equal("Radarr", link.Server);
    }

    /// <summary>
    /// The suggestions are what the viewer played last, newest first.
    /// True: the issue page offers the titles the viewer most likely means.
    /// False: the suggestions would be unplayed or out of order.
    /// </summary>
    [Fact]
    public void Suggestions_Played_NewestFirst()
    {
        var older = Movie("Paper Harbor", 2021);
        var never = Movie("Quiet Orchard", 2017);
        _h.Played[older.Id] = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        _h.Played[_movie.Id] = new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);
        _h.Found = new List<BaseItem> { older, never, _movie };

        var suggested = _h.Issues.Suggestions(_h.Access(_viewer));

        Assert.Equal(new[] { "The Lighthouse Keeper (2019)", "Paper Harbor (2021)" }, suggested.Select(i => i.Title));
    }

    /// <summary>
    /// A report sends the created event about its first reporter, worded for the reporters' and managers' eyes.
    /// True: Notifications hears each report with what was reported and by whom.
    /// False: managers would never hear of a report, or it would read as something else.
    /// </summary>
    [Fact]
    public void Report_Event_IsCreatedAboutTheFirstReporter()
    {
        var issue = Report(_viewer);
        _h.Issues.Join(_h.Access(_other), issue.Id, new IssueSubmit { Type = IssueType.Video });

        var reported = Assert.Single(_h.Events.Sent);

        Assert.Equal(IssueEvents.Created, reported.Key);
        Assert.Equal(("Audio issue reported", "viewer reported The Lighthouse Keeper (2019). The sound drops out."), (reported.Title, reported.Body));
        Assert.Equal(_viewer.Id, reported.UserId);
        Assert.Equal("issues/#/item?id=" + _movie.Id.ToString("N"), reported.Url);
        Assert.Equal(NectoEventLevel.Warning, reported.Level);
    }

    /// <summary>
    /// A resolved or declined issue sends the updated event, naming the decision and the manager's note, with every reporter.
    /// True: each reporter can hear their issue was resolved or declined, and why.
    /// False: a decline would read as resolved, or a joined reporter would be left out.
    /// </summary>
    [Fact]
    public void Decide_ResolveAndDecline_SendTheUpdatedEvent()
    {
        var issue = Report(_viewer);
        _h.Issues.Join(_h.Access(_other), issue.Id, new IssueSubmit { Type = IssueType.Video });

        _h.Issues.Decide(_h.Access(_admin), issue.Id, IssueAction.Resolve, "Replaced the file.");
        _h.Issues.Decide(_h.Access(_admin), issue.Id, IssueAction.Reopen, null);
        _h.Issues.Decide(_h.Access(_admin), issue.Id, IssueAction.Decline, "Plays fine here.");

        var updates = _h.Events.Sent.Where(e => e.Key == IssueEvents.Updated).ToList();
        Assert.Equal(2, updates.Count);
        Assert.Equal(("Issue resolved", "The issue with The Lighthouse Keeper (2019) was resolved. Replaced the file."), (updates[0].Title, updates[0].Body));
        Assert.Equal(("Issue declined", "The issue with The Lighthouse Keeper (2019) was declined. Plays fine here."), (updates[1].Title, updates[1].Body));
        Assert.Equal(string.Join(',', new[] { _viewer.Id, _other.Id }.Select(i => i.ToString("N"))), updates[1].Values["reporterIds"]);
        Assert.Equal(issue.Id.ToString("N"), updates[1].Data["issueId"]);
        Assert.Equal("declined", updates[1].Values["state"]);
    }
}
