using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Siphonophore.Stolon;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Services.Issues.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Necto.Services.Issues;

/// <summary>
/// Every change an issue goes through: reported, joined, edited, withdrawn, decided by an administrator, and marked when
/// its item's file changes. Also finds the playable items the issue page lists.
/// </summary>
[PluginService(ServiceLifetime.Singleton)]
public sealed class IssueService
{
    /// <summary>The longest note kept.</summary>
    internal const int MaxNote = 1000;

    /// <summary>The kinds of item an issue can be about: anything that plays on its own.</summary>
    internal static readonly BaseItemKind[] Playable =
    {
        BaseItemKind.Movie, BaseItemKind.Episode, BaseItemKind.Video, BaseItemKind.MusicVideo, BaseItemKind.Audio, BaseItemKind.AudioBook
    };

    // How many suggestions the empty search lists, and how many items a search finds.
    private const int SuggestionCount = 20;
    private const int SearchCount = 200;

    private readonly RequestDatabase _db;
    private readonly ILibraryManager _library;
    private readonly IUserDataManager _userData;
    private readonly ArrClient _arr;
    private readonly RequestNotifier _notifier;
    private readonly ActivityLogger _activity;
    private readonly ILogger<IssueService> _logger;
    private readonly TimeProvider _time;

    /// <summary>
    /// Initializes a new instance of the <see cref="IssueService"/> class.
    /// </summary>
    /// <param name="db">The database the issues are kept in.</param>
    /// <param name="library">The library, for the items.</param>
    /// <param name="userData">What each user played, for the suggestions.</param>
    /// <param name="arr">The Radarr and Sonarr client, for where an item is in them.</param>
    /// <param name="notifier">The notifier.</param>
    /// <param name="activity">The activity log writer.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="time">The clock, or the system's when null.</param>
    public IssueService(RequestDatabase db, ILibraryManager library, IUserDataManager userData, ArrClient arr, RequestNotifier notifier, ActivityLogger activity, ILogger<IssueService> logger, TimeProvider? time = null)
    {
        _db = db;
        _library = library;
        _userData = userData;
        _arr = arr;
        _notifier = notifier;
        _activity = activity;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Gets a value indicating whether issues are on.</summary>
    public static bool Enabled => RequestConfig.Instance?.ReadIssues(c => c.Enabled) == true;

    // ------------------------------------------------------------------ items

    /// <summary>Gets whether an item is one an issue can be about: a playable file of its own, not a folder.</summary>
    /// <param name="item">The library item.</param>
    /// <returns><c>true</c> for a movie, episode, video, music video, song or audiobook that is really there.</returns>
    internal static bool IsPlayable(BaseItem? item)
        => item is not null && !item.IsFolder && !item.IsVirtualItem && Playable.Contains(item.GetBaseItemKind());

    /// <summary>Gets the playable library item an issue can be reported on, when the viewer may see it.</summary>
    /// <param name="itemId">The library item.</param>
    /// <param name="access">The viewer.</param>
    /// <returns>The item, or null when it is gone, not playable, or not theirs to see.</returns>
    public BaseItem? Find(Guid itemId, UserAccess access)
    {
        ArgumentNullException.ThrowIfNull(access);
        var item = itemId == Guid.Empty ? null : _library.GetItemById(itemId);
        return IsPlayable(item) && Sees(item!, access) ? item : null;
    }

    /// <summary>Lists what the viewer played last, newest first, for the empty search's suggestions.</summary>
    /// <param name="access">The viewer.</param>
    /// <returns>The items.</returns>
    public List<IssueItem> Suggestions(UserAccess access)
    {
        ArgumentNullException.ThrowIfNull(access);
        if (access.User is not { } user)
        {
            return new List<IssueItem>();
        }

        var query = new InternalItemsQuery(user)
        {
            IncludeItemTypes = Playable,
            Recursive = true,
            IsVirtualItem = false,
            OrderBy = new[] { (ItemSortBy.DatePlayed, SortOrder.Descending) },
            Limit = SuggestionCount * 3
        };

        // Sorted by when each was played, but an item never played sorts among them on some servers, so it is left out here.
        return _library.GetItemList(query)
            .Where(i => IsPlayable(i) && Sees(i, access))
            .Select(i => (Item: i, Played: _userData.GetUserData(user, i)?.LastPlayedDate))
            .Where(p => p.Played is not null)
            .OrderByDescending(p => p.Played)
            .Take(SuggestionCount)
            .Select(p => ToItem(p.Item, access.Language))
            .ToList();
    }

    /// <summary>Finds the playable items the viewer may see whose name matches a search.</summary>
    /// <param name="access">The viewer.</param>
    /// <param name="term">What they typed.</param>
    /// <returns>The items, as the library ranks them.</returns>
    public List<IssueItem> Search(UserAccess access, string? term)
    {
        ArgumentNullException.ThrowIfNull(access);
        term = (term ?? string.Empty).Trim();
        if (term.Length == 0 || access.User is not { } user)
        {
            return new List<IssueItem>();
        }

        var query = new InternalItemsQuery(user)
        {
            SearchTerm = term.Length > 200 ? term[..200] : term,
            IncludeItemTypes = Playable,
            Recursive = true,
            IsVirtualItem = false,
            Limit = SearchCount
        };
        return _library.GetItemList(query).Where(i => IsPlayable(i) && Sees(i, access)).Select(i => ToItem(i, access.Language)).ToList();
    }

    /// <summary>Gets what the issue page shows for an item: its open issue, mine or someone else's, and its closed ones.</summary>
    /// <param name="access">The viewer.</param>
    /// <param name="item">The library item, which the viewer may see.</param>
    /// <returns>The view.</returns>
    public IssueView View(UserAccess access, BaseItem item)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(item);
        var issues = _db.IssuesForItem(item.Id);
        var open = issues.FirstOrDefault(i => i.IsOpen);
        var mine = open is not null && open.HasReporter(access.UserId) ? open : null;
        return new IssueView
        {
            Item = ToItem(item, access.Language),
            Mine = mine is null ? null : ForViewer(mine, access),
            Open = open is not null && mine is null ? ForViewer(open, access) : null,
            Closed = issues.Where(i => !i.IsOpen).Select(i => ForViewer(i, access)).ToList(),
            Refusal = mine is null ? Refusal(access) : null
        };
    }

    /// <summary>Turns a library item into what the issue page shows of it.</summary>
    /// <param name="item">The library item.</param>
    /// <param name="language">The language its title is written in, or null for US English.</param>
    /// <returns>The item.</returns>
    internal static IssueItem ToItem(BaseItem item, string? language)
    {
        ArgumentNullException.ThrowIfNull(item);
        var episode = item as Episode;
        var image = item.HasImage(ImageType.Primary) ? item.GetImageInfo(ImageType.Primary, 0) : null;
        var series = episode?.Series;
        var seriesImage = series is not null && series.HasImage(ImageType.Primary) ? series.GetImageInfo(ImageType.Primary, 0) : null;
        var snapshot = Snapshot(item);
        return new IssueItem
        {
            Id = item.Id,
            Type = item.GetBaseItemKind().ToString(),
            Name = item.Name ?? string.Empty,
            Title = Describe(snapshot, language),
            ParentName = episode?.SeriesName ?? ((item as Audio)?.AlbumArtists is { Count: > 0 } artists ? artists[0] : null),
            SeriesId = episode?.SeriesId is { } id && id != Guid.Empty ? id : null,
            SeasonNumber = episode?.ParentIndexNumber,
            EpisodeNumber = episode?.IndexNumber,
            Year = item.ProductionYear,
            ImageTag = image is null ? null : Tag(image),
            ImageAspectRatio = image is { Width: > 0, Height: > 0 } ? (double)image.Width / image.Height : item.GetDefaultPrimaryImageAspectRatio() is > 0 and var ratio ? ratio : null,
            SeriesImageTag = seriesImage is null ? null : Tag(seriesImage)
        };
    }

    // A picture's tag, from when it last changed, so a browser keeps it until it does.
    private static string Tag(ItemImageInfo image) => image.DateModified.Ticks.ToString("x", CultureInfo.InvariantCulture);

    /// <summary>Takes a new issue's snapshot of an item: what it is, its file, and the ids Radarr and Sonarr know it by.</summary>
    /// <param name="item">The library item.</param>
    /// <returns>An issue with the item's fields filled in and nothing else.</returns>
    internal static Issue Snapshot(BaseItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var episode = item as Episode;
        return new Issue
        {
            ItemId = item.Id,
            ItemType = item.GetBaseItemKind().ToString(),
            Title = item.Name ?? string.Empty,
            SeriesName = episode?.SeriesName,
            SeriesId = episode?.SeriesId is { } id && id != Guid.Empty ? id : null,
            SeasonNumber = episode?.ParentIndexNumber,
            EpisodeNumber = episode?.IndexNumber,
            Year = item.ProductionYear,
            Path = item.Path,
            FileSize = item.Size,
            FileModifiedUtc = item.DateModified == default ? null : DateTime.SpecifyKind(item.DateModified, DateTimeKind.Utc),
            ProviderIds = ProviderIds.Clean(item.ProviderIds),
            SeriesProviderIds = episode?.Series is { } series ? ProviderIds.Clean(series.ProviderIds) : null
        };
    }

    /// <summary>
    /// Says what an issue is about, as it reads in lists and updates: "Show · S1E2" for an episode, and the name with its
    /// year for anything else.
    /// </summary>
    /// <param name="issue">The issue, or a snapshot.</param>
    /// <param name="language">The language, or null for US English.</param>
    /// <returns>The title.</returns>
    internal static string Describe(Issue issue, string? language)
    {
        ArgumentNullException.ThrowIfNull(issue);
        if (issue.ItemType == nameof(BaseItemKind.Episode) && !string.IsNullOrEmpty(issue.SeriesName) && issue.SeasonNumber is { } season && issue.EpisodeNumber is { } number)
        {
            return StolonTranslations.Format(TranslationKeys.IssuesEpisodeTitleFormat, language, issue.SeriesName, season, number);
        }

        // A name that already ends in its year, as one read from its file's name can, isn't given it twice.
        var suffix = issue.Year is { } year ? " (" + year.ToString(CultureInfo.InvariantCulture) + ")" : null;
        return suffix is not null && issue.ItemType is nameof(BaseItemKind.Movie) or nameof(BaseItemKind.MusicVideo) && !issue.Title.EndsWith(suffix, StringComparison.Ordinal)
            ? issue.Title + suffix
            : issue.Title;
    }

    /// <summary>
    /// Gets an issue's title for its updates: an episode as its show, its season and episode numbers in two digits, and
    /// its own name, as "Show - S01E02 · Name". Anything else reads as <see cref="Describe"/> has it.
    /// </summary>
    /// <param name="issue">The issue.</param>
    /// <param name="language">The language, or null for US English.</param>
    /// <returns>The title.</returns>
    internal static string NotifyTitle(Issue issue, string? language)
    {
        ArgumentNullException.ThrowIfNull(issue);
        if (issue.ItemType == nameof(BaseItemKind.Episode) && !string.IsNullOrEmpty(issue.SeriesName) && issue.SeasonNumber is { } season && issue.EpisodeNumber is { } number && !string.IsNullOrEmpty(issue.Title))
        {
            return StolonTranslations.Format(
                TranslationKeys.IssuesNotifyEpisodeTitle,
                language,
                issue.SeriesName,
                season.ToString("00", CultureInfo.InvariantCulture),
                number.ToString("00", CultureInfo.InvariantCulture),
                issue.Title);
        }

        return Describe(issue, language);
    }

    /// <summary>Gets what an issue type is called, in a language.</summary>
    /// <param name="type">The issue type.</param>
    /// <param name="language">The language, or null for US English.</param>
    /// <returns>Its name.</returns>
    internal static string TypeName(IssueType type, string? language) => StolonTranslations.Get(
        type switch
        {
            IssueType.Video => TranslationKeys.Video,
            IssueType.Audio => TranslationKeys.Audio,
            IssueType.Subtitles => TranslationKeys.Subtitles,
            IssueType.Metadata => TranslationKeys.IssuesTypeMetadata,
            IssueType.WontPlay => TranslationKeys.IssuesTypeWontPlay,
            _ => TranslationKeys.Other
        },
        language);

    // Whether the viewer may see an item: administrators see everything, anyone else what their libraries and rating allow.
    private static bool Sees(BaseItem item, UserAccess access)
        => access.ManagesIssues || (access.User is { } user && LibraryMatcher.IsVisibleTo(item, user));

    /// <summary>
    /// Gets an issue as a viewer sees it. Only administrators see where its file is and who reported it. Anyone else
    /// sees their own report and notes, what the others reported without their names, and the administrators' notes
    /// without who wrote them.
    /// </summary>
    /// <param name="issue">The issue.</param>
    /// <param name="access">The viewer.</param>
    /// <returns>The issue, or a copy with what the viewer may see.</returns>
    internal static Issue ForViewer(Issue issue, UserAccess access)
    {
        ArgumentNullException.ThrowIfNull(issue);
        ArgumentNullException.ThrowIfNull(access);
        if (access.ManagesIssues)
        {
            return issue;
        }

        var copy = Copy(issue);
        copy.Path = null;
        copy.FileSize = null;
        copy.FileModifiedUtc = null;
        copy.DecidedBy = null;
        var self = access.User?.Id ?? Guid.Empty;
        foreach (var reporter in copy.Reporters.Where(r => r.UserId != self))
        {
            reporter.UserId = Guid.Empty;
            reporter.UserName = string.Empty;
        }

        copy.Notes = copy.Notes.Where(n => n.Admin || n.UserId == self).ToList();
        foreach (var note in copy.Notes.Where(n => n.Admin))
        {
            note.UserId = Guid.Empty;
            note.User = string.Empty;
        }

        return copy;
    }

    private static Issue Copy(Issue issue) => System.Text.Json.JsonSerializer.Deserialize<Issue>(System.Text.Json.JsonSerializer.Serialize(issue))!;

    // ------------------------------------------------------------------ reporters

    /// <summary>Says why someone cannot report or join an issue now, or null when they can.</summary>
    /// <param name="access">The user.</param>
    /// <returns>The reason, in their language, or null.</returns>
    public string? Refusal(UserAccess access)
    {
        ArgumentNullException.ThrowIfNull(access);
        if (!access.ReportIssues)
        {
            return Say(access, TranslationKeys.IssuesErrorCannotReport);
        }

        var permissions = access.Permissions;
        if (access.ManagesIssues || permissions is null || permissions.IssueQuotaLimit <= 0)
        {
            return null;
        }

        var since = _time.GetUtcNow().UtcDateTime.AddDays(-permissions.IssueQuotaDays);
        return _db.CountReportsSince(access.UserId, since) >= permissions.IssueQuotaLimit ? Say(access, TranslationKeys.IssuesErrorLimit) : null;
    }

    /// <summary>Reports a new issue with an item. Refused when the item already has an open issue.</summary>
    /// <param name="access">The reporter.</param>
    /// <param name="submit">The item, what is wrong, and a note.</param>
    /// <returns>The issue, or why it was refused.</returns>
    public (Issue? Issue, string? Error) Report(UserAccess access, IssueSubmit submit)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(submit);
        if (Check(access, submit) is { } invalid)
        {
            return (null, invalid);
        }

        if (Find(submit.ItemId, access) is not { } item)
        {
            return (null, Say(access, TranslationKeys.IssuesErrorItemNotFound));
        }

        var now = _time.GetUtcNow().UtcDateTime;
        var note = Clip(submit.Note);
        var (issue, error) = _db.Atomic<(Issue?, string?)>(() =>
        {
            if (_db.IssuesForItem(item.Id).FirstOrDefault(i => i.IsOpen) is { } open)
            {
                return (null, Say(access, open.HasReporter(access.UserId) ? TranslationKeys.IssuesErrorAlreadyReported : TranslationKeys.IssuesErrorJoinInstead));
            }

            if (Refusal(access) is { } refused)
            {
                return (null, refused);
            }

            var created = Snapshot(item);
            created.Id = Guid.NewGuid();
            created.State = IssueState.Reported;
            created.CreatedUtc = now;
            created.UpdatedUtc = now;
            created.Reporters.Add(new IssueReporter { UserId = access.UserId, UserName = access.UserName, Type = submit.Type, ReportedUtc = now });
            if (note is not null)
            {
                created.Notes.Add(new IssueNote { UserId = access.UserId, User = access.UserName, Text = note, Time = now });
            }

            _db.InsertIssue(created);
            return (created, null);
        });

        if (issue is null)
        {
            return (null, error);
        }

        Log(TranslationKeys.IssuesEventReported, "IssueReported", TranslationKeys.IssuesActivityReported, access, issue, KindName(submit.Type));
        _notifier.NotifyIssue(NotificationEvent.IssueReported, issue);
        return (issue, null);
    }

    /// <summary>Joins someone else's open issue, with what the joiner says is wrong and their note.</summary>
    /// <param name="access">The joiner.</param>
    /// <param name="issueId">The issue.</param>
    /// <param name="submit">What is wrong, and a note.</param>
    /// <returns>The issue, or why it was refused.</returns>
    public (Issue? Issue, string? Error) Join(UserAccess access, Guid issueId, IssueSubmit submit)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(submit);
        if (Check(access, submit) is { } invalid)
        {
            return (null, invalid);
        }

        var now = _time.GetUtcNow().UtcDateTime;
        var note = Clip(submit.Note);
        var (issue, error) = _db.Atomic<(Issue?, string?)>(() =>
        {
            if (Visible(issueId, access) is not { } found)
            {
                return (null, Say(access, TranslationKeys.IssuesErrorNotFound));
            }

            if (!found.IsOpen)
            {
                return (null, Say(access, TranslationKeys.IssuesErrorClosed));
            }

            if (found.HasReporter(access.UserId))
            {
                return (null, Say(access, TranslationKeys.IssuesErrorAlreadyReported));
            }

            if (Refusal(access) is { } refused)
            {
                return (null, refused);
            }

            found.Reporters.Add(new IssueReporter { UserId = access.UserId, UserName = access.UserName, Type = submit.Type, ReportedUtc = now });
            if (note is not null)
            {
                found.Notes.Add(new IssueNote { UserId = access.UserId, User = access.UserName, Text = note, Time = now });
            }

            found.UpdatedUtc = now;
            _db.UpdateIssue(found);
            return (found, null);
        });

        if (issue is not null)
        {
            Log(TranslationKeys.IssuesEventReported, "IssueReported", TranslationKeys.IssuesActivityJoined, access, issue, KindName(submit.Type));
        }

        return (issue, error);
    }

    /// <summary>Changes what a reporter says is wrong with an open issue, and their own note. A blank note takes it out.</summary>
    /// <param name="access">The reporter.</param>
    /// <param name="issueId">The issue.</param>
    /// <param name="submit">What is wrong, and the note.</param>
    /// <returns>The issue, or why it was refused.</returns>
    public (Issue? Issue, string? Error) Edit(UserAccess access, Guid issueId, IssueSubmit submit)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(submit);
        if (Check(access, submit, editing: true) is { } invalid)
        {
            return (null, invalid);
        }

        var now = _time.GetUtcNow().UtcDateTime;
        var note = Clip(submit.Note);
        return _db.Atomic<(Issue?, string?)>(() =>
        {
            if (Visible(issueId, access) is not { } found || !found.HasReporter(access.UserId))
            {
                return (null, Say(access, TranslationKeys.IssuesErrorNotFound));
            }

            if (!found.IsOpen)
            {
                return (null, Say(access, TranslationKeys.IssuesErrorClosed));
            }

            found.Reporters.First(r => r.UserId == access.UserId).Type = submit.Type;
            var own = found.Notes.FindIndex(n => n.UserId == access.UserId && !n.Admin);
            if (note is null)
            {
                found.Notes.RemoveAll(n => n.UserId == access.UserId && !n.Admin);
            }
            else if (own >= 0)
            {
                found.Notes[own].Text = note;
                found.Notes[own].Time = now;
                found.Notes.RemoveAll(n => n.UserId == access.UserId && !n.Admin && !ReferenceEquals(n, found.Notes[own]));
            }
            else
            {
                found.Notes.Add(new IssueNote { UserId = access.UserId, User = access.UserName, Text = note, Time = now });
            }

            found.UpdatedUtc = now;
            _db.UpdateIssue(found);
            return (found, null);
        });
    }

    /// <summary>
    /// Takes a reporter off an open issue. The only reporter rescinds it, which closes it. Anyone else who reported or
    /// joined it leaves it, with their note, and the issue stays open for the rest.
    /// </summary>
    /// <param name="access">The reporter.</param>
    /// <param name="issueId">The issue.</param>
    /// <returns>The issue as it now stands, or why it was refused.</returns>
    public (Issue? Issue, string? Error) Withdraw(UserAccess access, Guid issueId)
    {
        ArgumentNullException.ThrowIfNull(access);
        var now = _time.GetUtcNow().UtcDateTime;
        var (issue, rescinded, error) = _db.Atomic<(Issue?, bool, string?)>(() =>
        {
            if (Visible(issueId, access) is not { } found || !found.HasReporter(access.UserId))
            {
                return (null, false, Say(access, TranslationKeys.IssuesErrorNotFound));
            }

            if (!found.IsOpen)
            {
                return (null, false, Say(access, TranslationKeys.IssuesErrorClosed));
            }

            var alone = found.Reporters.All(r => r.UserId == access.UserId);
            if (alone)
            {
                found.State = IssueState.Rescinded;
            }
            else
            {
                found.Reporters.RemoveAll(r => r.UserId == access.UserId);
                found.Notes.RemoveAll(n => n.UserId == access.UserId && !n.Admin);
            }

            found.UpdatedUtc = now;
            _db.UpdateIssue(found);
            return (found, alone, null);
        });

        if (issue is not null && rescinded)
        {
            _activity.Log(
                ServerWording.Get(TranslationKeys.IssuesActivityRescindedName),
                RequestService.ActivityType + ".IssueRescinded",
                ActivityText.Sentence(ServerWording.Format(TranslationKeys.IssuesActivityRescinded, access.UserName, Describe(issue, ServerWording.Language))),
                userId: access.UserId);
        }

        return (issue, error);
    }

    // A submission's own checks: the user may report, the type is one there is, and Other comes with a note.
    private static string? Check(UserAccess access, IssueSubmit submit, bool editing = false)
    {
        if (!access.ReportIssues && !editing)
        {
            return Say(access, TranslationKeys.IssuesErrorCannotReport);
        }

        if (!Enum.IsDefined(submit.Type))
        {
            return Say(access, TranslationKeys.IssuesErrorType);
        }

        return submit.Type == IssueType.Other && Clip(submit.Note) is null ? Say(access, TranslationKeys.IssuesErrorNoteRequired) : null;
    }

    // An issue whose item the user may see, or one whose item is gone, which hides nothing.
    private Issue? Visible(Guid issueId, UserAccess access)
    {
        var issue = _db.GetIssue(issueId);
        if (issue is null || access.ManagesIssues)
        {
            return issue;
        }

        var item = _library.GetItemById(issue.ItemId);
        return item is null || Sees(item, access) ? issue : null;
    }

    // ------------------------------------------------------------------ administrators

    /// <summary>
    /// Decides an issue: confirms it, resolves it, declines it, or opens a closed one again, with an optional note the
    /// reporters see. Resolving and declining tell the reporters.
    /// </summary>
    /// <param name="admin">The administrator.</param>
    /// <param name="issueId">The issue.</param>
    /// <param name="action">What to do.</param>
    /// <param name="note">The administrator's note, or null.</param>
    /// <returns>The issue, or why it was refused.</returns>
    public (Issue? Issue, string? Error) Decide(UserAccess admin, Guid issueId, IssueAction action, string? note)
    {
        ArgumentNullException.ThrowIfNull(admin);
        var now = _time.GetUtcNow().UtcDateTime;
        var text = Clip(note);
        var (issue, error) = _db.Atomic<(Issue?, string?)>(() =>
        {
            if (_db.GetIssue(issueId) is not { } found)
            {
                return (null, Say(admin, TranslationKeys.IssuesErrorNotFound));
            }

            var allowed = action switch
            {
                IssueAction.Confirm => found.State == IssueState.Reported,
                IssueAction.Resolve or IssueAction.Decline => found.IsOpen,
                _ => !found.IsOpen
            };
            if (!allowed)
            {
                return (null, Say(admin, action switch
                {
                    IssueAction.Reopen => TranslationKeys.IssuesErrorAlreadyOpen,
                    IssueAction.Confirm when found.State == IssueState.Confirmed => TranslationKeys.IssuesErrorAlreadyConfirmed,
                    _ => TranslationKeys.IssuesErrorNotOpen
                }));
            }

            if (action == IssueAction.Reopen)
            {
                if (_db.IssuesForItem(found.ItemId).Any(i => i.IsOpen && i.Id != found.Id))
                {
                    return (null, Say(admin, TranslationKeys.IssuesErrorOpenOnItem));
                }

                // Open again, it is about the file as it is now.
                if (_library.GetItemById(found.ItemId) is { } item && IsPlayable(item))
                {
                    var snapshot = Snapshot(item);
                    (found.Path, found.FileSize, found.FileModifiedUtc) = (snapshot.Path, snapshot.FileSize, snapshot.FileModifiedUtc);
                    found.FileChanged = false;
                }
            }

            found.State = action switch
            {
                IssueAction.Confirm => IssueState.Confirmed,
                IssueAction.Resolve => IssueState.Resolved,
                IssueAction.Decline => IssueState.Declined,
                _ => IssueState.Reported
            };
            found.DecidedBy = admin.UserName;
            if (text is not null)
            {
                found.Notes.Add(new IssueNote { UserId = admin.UserId, User = admin.UserName, Text = text, Time = now, Admin = true });
            }

            found.UpdatedUtc = now;
            _db.UpdateIssue(found);
            return (found, null);
        });

        if (issue is null)
        {
            return (null, error);
        }

        var (name, type, overview) = action switch
        {
            IssueAction.Confirm => (TranslationKeys.IssuesBadgeConfirmed, "IssueConfirmed", TranslationKeys.IssuesActivityConfirmed),
            IssueAction.Resolve => (TranslationKeys.IssuesEventResolved, "IssueResolved", TranslationKeys.IssuesActivityResolved),
            IssueAction.Decline => (TranslationKeys.IssuesActivityDeclinedName, "IssueDeclined", TranslationKeys.IssuesActivityDeclined),
            _ => (TranslationKeys.IssuesActivityReopenedName, "IssueReopened", TranslationKeys.IssuesActivityReopened)
        };
        Log(name, type, overview, admin, issue, null);
        if (action is IssueAction.Resolve or IssueAction.Decline)
        {
            _notifier.NotifyIssue(NotificationEvent.IssueResolved, issue);
        }

        return (issue, null);
    }

    /// <summary>
    /// Gets where an administrator opens an issue's title in Radarr or Sonarr: a movie's page in the Radarr that has it,
    /// an episode's show in the Sonarr that has it. Null for anything else, or a title neither has.
    /// </summary>
    /// <param name="issue">The issue.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The page and the server's name, or null.</returns>
    public async Task<ArrLink?> ArrLinkAsync(Issue issue, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(issue);
        return ArrTarget(issue) is { } target ? await _arr.HoldingLinkAsync(target.Type, target.Ids, cancellationToken).ConfigureAwait(false) : null;
    }

    /// <summary>
    /// Picks what an issue is looked up by in Radarr or Sonarr: a movie by its own ids in Radarr, an episode by its
    /// show's ids in Sonarr. Null for anything else, which neither keeps.
    /// </summary>
    /// <param name="issue">The issue.</param>
    /// <returns>The type of server and the ids, or null.</returns>
    internal static (RequestType Type, Dictionary<string, string> Ids)? ArrTarget(Issue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        return issue.ItemType switch
        {
            nameof(BaseItemKind.Movie) when issue.ProviderIds.Count > 0 => (RequestType.Movie, issue.ProviderIds),
            nameof(BaseItemKind.Episode) when issue.SeriesProviderIds is { Count: > 0 } series => (RequestType.Series, series),
            _ => null
        };
    }

    // ------------------------------------------------------------------ upkeep

    /// <summary>
    /// Gets whether an item's file differs from the one an issue was reported on: another path, size, or time it was
    /// last changed.
    /// </summary>
    /// <param name="issue">The issue, with its file as reported.</param>
    /// <param name="item">The item as it is now, or null when it is gone.</param>
    /// <returns><c>true</c> when the file changed or is gone.</returns>
    internal static bool FileDiffers(Issue issue, BaseItem? item)
    {
        ArgumentNullException.ThrowIfNull(issue);
        if (item is null)
        {
            return true;
        }

        var now = Snapshot(item);
        return !string.Equals(issue.Path ?? string.Empty, now.Path ?? string.Empty, StringComparison.Ordinal)
            || (issue.FileSize is { } size && now.FileSize is { } current && size != current)
            || (issue.FileModifiedUtc is { } modified && now.FileModifiedUtc is { } changed && Math.Abs((changed - modified).TotalSeconds) >= 1);
    }

    /// <summary>
    /// Marks the open issues whose item's file was replaced or rescanned since they were reported, for an
    /// administrator to look at. Nothing is resolved by it.
    /// </summary>
    /// <param name="itemIds">Only the issues about these items, or every open issue when null.</param>
    /// <returns>How many were marked.</returns>
    public int CheckFiles(IReadOnlyCollection<Guid>? itemIds = null)
    {
        var marked = 0;
        var now = _time.GetUtcNow().UtcDateTime;
        foreach (var issue in _db.OpenIssues().Where(i => !i.FileChanged && (itemIds is null || itemIds.Contains(i.ItemId))))
        {
            if (!FileDiffers(issue, _library.GetItemById(issue.ItemId)))
            {
                continue;
            }

            var changed = _db.Atomic(() =>
            {
                if (_db.GetIssue(issue.Id) is not { IsOpen: true, FileChanged: false } fresh)
                {
                    return false;
                }

                fresh.FileChanged = true;
                fresh.UpdatedUtc = now;
                _db.UpdateIssue(fresh);
                return true;
            });
            if (changed)
            {
                marked++;
                _logger.LogDebug("The file of {Title} changed since its issue was reported", FormatUtilities.SanitizeForLog(issue.Title));
            }
        }

        return marked;
    }

    /// <summary>Removes resolved, declined and rescinded issues older than <b>Keep closed issues (days)</b>.</summary>
    /// <returns>How many were removed.</returns>
    public int Clean()
    {
        var days = RequestConfig.Instance?.ReadIssues(c => c.RetentionDays) ?? 0;
        return days <= 0 ? 0 : _db.DeleteClosedIssuesBefore(_time.GetUtcNow().UtcDateTime.AddDays(-days));
    }

    // Writes one entry to the activity log, naming who did it, the title, and what they say is wrong when they report it.
    private void Log(string nameKey, string type, string overviewKey, UserAccess by, Issue issue, string? kind)
    {
        var overview = ServerWording.Format(overviewKey, by.UserName, Describe(issue, ServerWording.Language), kind);
        _activity.Log(ServerWording.Get(nameKey), RequestService.ActivityType + "." + type, ActivityText.Sentence(overview), userId: by.UserId);
    }

    // What a reporter says is wrong, as the activity log words it, such as "a video issue".
    private static string KindName(IssueType type) => ServerWording.Get(
        type switch
        {
            IssueType.Video => TranslationKeys.IssuesActivityKindVideo,
            IssueType.Audio => TranslationKeys.IssuesActivityKindAudio,
            IssueType.Subtitles => TranslationKeys.IssuesActivityKindSubtitles,
            IssueType.Metadata => TranslationKeys.IssuesActivityKindMetadata,
            IssueType.WontPlay => TranslationKeys.IssuesActivityKindWontPlay,
            _ => TranslationKeys.IssuesActivityKindOther
        });

    private static string Say(UserAccess access, string key) => StolonTranslations.Get(key, access.Language);

    private static string? Clip(string? text)
    {
        text = text?.Trim();
        return string.IsNullOrEmpty(text) ? null : text.Length > MaxNote ? text[..MaxNote] : text;
    }
}
