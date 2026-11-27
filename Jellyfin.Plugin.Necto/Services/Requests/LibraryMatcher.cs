using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Necto.Services.Requests;

/// <summary>
/// Finds the library movie or show a set of provider ids belongs to.
/// </summary>
[PluginService(ServiceLifetime.Singleton)]
public partial class LibraryMatcher
{
    private readonly ILibraryManager _libraryManager;
    private readonly Microsoft.Extensions.Logging.ILogger<LibraryMatcher>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryMatcher"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="userData">The user data manager.</param>
    /// <param name="userManager">The user manager, for a user's library access changing.</param>
    /// <param name="logger">The logger.</param>
    public LibraryMatcher(ILibraryManager libraryManager, IUserDataManager userData, IUserManager userManager, Microsoft.Extensions.Logging.ILogger<LibraryMatcher>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(userManager);
        _logger = logger;
        _libraryManager = libraryManager;

        // A user's library access or parental rating changing changes what they may see, so what was kept per viewer
        // is forgotten.
        userManager.OnUserUpdated += (_, _) =>
        {
            _seenPeople.Clear();
            _visible.Clear();
        };

        // What's kept is forgotten as the library changes, only as far as the change can matter: scans, image and
        // trickplay tasks update items constantly, and most of them can't change a match.
        _libraryManager.ItemAdded += (_, e) => OnLibraryChanged(e.Item, comeOrGone: true);
        _libraryManager.ItemRemoved += (_, e) => OnLibraryChanged(e.Item, comeOrGone: true, gone: true);
        _libraryManager.ItemUpdated += (_, e) => OnLibraryChanged(e.Item, comeOrGone: false);
    }

    // Each show's episodes by season, as last counted.
    private readonly ConcurrentDictionary<Guid, (DateTime AtUtc, SeasonCounts Counts)> _counts = new();

    // Whether something a person is in may be seen, as last asked: by the person and what the viewer may see (their
    // libraries, parental rating and tags), so everyone allowed the same shares one answer.
    private readonly ConcurrentDictionary<string, (DateTime AtUtc, bool Seen)> _seenPeople = new(StringComparer.Ordinal);

    // Who may see what, as last decided, shared by every item the answer can't differ for (see VisibilityKey).
    private static readonly ConcurrentDictionary<string, (DateTime AtUtc, bool Visible)> _visible = new(StringComparer.Ordinal);

    // A movie or show changing may change a match; a show, season or episode changing changes that show's counts;
    // any of them may change who appears in what a viewer may see.
    private void OnLibraryChanged(BaseItem item, bool comeOrGone, bool gone = false)
    {
        // A movie or show is kept in the index as it is now. One updated without a change to anything a match or who
        // sees it depends on (as scans, image and metadata refreshes mostly are) leaves what's kept alone; until the
        // library has been read, every update counts as a change.
        if (item is Movie or Series)
        {
            var ready = Indexed;
            var changed = gone ? UnindexItem(item.Id) : IndexItem(item);
            if (!comeOrGone && ready && !changed)
            {
                if (item is Series same)
                {
                    _counts.TryRemove(same.Id, out _);
                }

                return;
            }
        }

        // Something new can only make a person newly seen, which the ten minutes an answer is kept catches up with; so
        // what's kept of people is forgotten only for what's gone or changed (its rating, tags or library), which can
        // hide someone. Asked about after every episode a scan adds, forgetting nothing is checked for first.
        var hides = gone || !comeOrGone;
        switch (item)
        {
            case Movie:
                ForgetSeen(hides);
                break;
            case Series series:
                ForgetSeen(hides);
                _counts.TryRemove(series.Id, out _);
                break;
            case Season season:
                _counts.TryRemove(season.SeriesId, out _);
                break;
            case Episode episode:
                _counts.TryRemove(episode.SeriesId, out _);
                // An episode going may take the only thing someone's in that a viewer may see; an episode's update
                // (every scan touches them) is left to the ten minutes a person's answer is kept.
                if (gone && !_seenPeople.IsEmpty)
                {
                    _seenPeople.Clear();
                }

                break;
            case ICollectionFolder:
                // A library added, removed or changed (its tags, say) changes who sees what is in it.
                _visible.Clear();
                break;
        }
    }

    // A movie or show that changed or went: who appears in what, and who may see what, as kept, may no longer hold.
    // One added changes neither for anything already kept.
    private void ForgetSeen(bool hides)
    {
        if (!hides)
        {
            return;
        }

        if (!_seenPeople.IsEmpty)
        {
            _seenPeople.Clear();
        }

        if (!_visible.IsEmpty)
        {
            _visible.Clear();
        }
    }

    // How long whether a viewer sees a person is kept while nothing it depends on changes.
    private static readonly TimeSpan FoundFor = TimeSpan.FromMinutes(10);

    // How long a show's episode counts are kept: forgotten as soon as the show, a season or an episode of it changes,
    // so this only bounds how long an answer could be out of step with a change that was somehow missed.
    private static readonly TimeSpan CountedFor = TimeSpan.FromHours(6);

    // How long who may see an item is kept: a user changing, or a movie, show or library changing, forgets it at once;
    // the rest (an episode or season re-rated) catches up within this.
    private static readonly TimeSpan VisibleFor = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Returns the library movie or show this title is, or null when the library does not have it. Provider
    /// ids decide when they match; otherwise the same title (or original title) and year counts, so an item
    /// Jellyfin never matched to TMDb is still found. An item whose ids contradict the title's is never a
    /// title match, since remakes and same-named shows share titles and years.
    /// </summary>
    /// <param name="type">Whether to look among movies or shows.</param>
    /// <param name="ids">The provider ids to match.</param>
    /// <param name="title">The title, for the fallback.</param>
    /// <param name="year">The release year, for the fallback. Without one there is no fallback, since remakes share titles.</param>
    /// <param name="user">Only items this user may see, with their library access and parental rating; every item when null.</param>
    /// <param name="titleLimit">The most library items to compare by title in the fallback.</param>
    /// <returns>The item, or null.</returns>
    public BaseItem? Find(RequestType type, IReadOnlyDictionary<string, string> ids, string? title = null, int? year = null, User? user = null, int titleLimit = 25)
    {
        var clean = ProviderIds.Clean(ids);
        if (FindIndexed(type, clean, title, year, user) is { Answered: true } kept)
        {
            return kept.Item;
        }

        if (clean.Count > 0)
        {
            var query = Query(user, type);
            query.HasAnyProviderId = clean;
            query.Limit = 1;
            var items = _libraryManager.GetItemList(query);
            if (items.Count > 0)
            {
                return items[0];
            }
        }

        if (string.IsNullOrWhiteSpace(title) || !year.HasValue)
        {
            return null;
        }

        var byTitle = Query(user, type);
        byTitle.SearchTerm = title;
        byTitle.Limit = titleLimit;
        return _libraryManager.GetItemList(byTitle).FirstOrDefault(i => IsTitleMatch(i, clean, title, year.Value));
    }

    /// <summary>
    /// Returns whether a library item is this title by name and year alone, with no provider id saying otherwise.
    /// </summary>
    /// <param name="item">The library item.</param>
    /// <param name="ids">The title's provider ids.</param>
    /// <param name="title">The title.</param>
    /// <param name="year">The year.</param>
    /// <returns>True when it is.</returns>
    public static bool IsTitleMatch(BaseItem item, IReadOnlyDictionary<string, string> ids, string title, int year)
    {
        ArgumentNullException.ThrowIfNull(item);
        return SameTitle(item, title, year) && !ProviderIds.Conflict(ProviderIds.Clean(item.ProviderIds), ids);
    }

    /// <summary>
    /// Returns whether a user may see a library item, with their library access and parental rating.
    /// </summary>
    /// <param name="item">The library item.</param>
    /// <param name="user">The user.</param>
    /// <returns>True when they may.</returns>
    public static bool IsVisibleTo(BaseItem item, User user)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(user);
        // IsVisible checks only the parental rating; IsVisibleStandalone also checks the user may open the library the
        // item is in, which is what hiding a library from someone means. It walks the item's folders and libraries each
        // time, and a page asks about many items in the same few places with the same few ratings, so the answer is
        // kept for a little while for everything it can't differ for.
        if (VisibilityKey(item, user) is not { } key)
        {
            return item.IsVisibleStandalone(user);
        }

        var now = DateTime.UtcNow;
        if (_visible.TryGetValue(key, out var kept) && now - kept.AtUtc < VisibleFor)
        {
            return kept.Visible;
        }

        if (_visible.Count >= 20000)
        {
            _visible.Clear();
        }

        var visible = item.IsVisibleStandalone(user);
        _visible[key] = (now, visible);
        return visible;
    }

    // What IsVisibleStandalone's answer for a movie, show, season or episode depends on, besides the user: the folder
    // it's in (which settles its parents, its library, its library's tags and options, and the country its rating is
    // read for), the season or show its rating falls back to, and its own rating, tags, country and type. Items alike
    // in all of those get the same answer, so they share one. Null for anything else (channels, collections, people),
    // which Jellyfin decides by more than this, and for an item in no folder.
    private static string? VisibilityKey(BaseItem item, User user)
    {
        if (item is not (Movie or Series or Season or Episode) || item.SourceType != SourceType.Library || item.ParentId == Guid.Empty)
        {
            return null;
        }

        return string.Join(
            "|",
            user.Id.ToString("N", CultureInfo.InvariantCulture),
            item.GetType().Name,
            item.ParentId.ToString("N", CultureInfo.InvariantCulture),
            item.DisplayParentId.ToString("N", CultureInfo.InvariantCulture),
            item.OfficialRating,
            item.CustomRating,
            item.PreferredMetadataCountryCode,
            string.Join(",", item.Tags ?? Array.Empty<string>()));
    }

    // What a user may see, as far as a library query for them is narrowed: the libraries and channels they may open,
    // their parental rating, which unrated items they're kept from, and their blocked and allowed tags. Users alike in
    // all of these see the same items.
    private static string SightKey(User user)
        => string.Join(
            "|",
            user.HasPermission(PermissionKind.IsAdministrator),
            user.HasPermission(PermissionKind.EnableAllFolders),
            string.Join(",", user.GetPreferenceValues<Guid>(PreferenceKind.EnabledFolders).Order()),
            string.Join(",", user.GetPreferenceValues<Guid>(PreferenceKind.BlockedMediaFolders).Order()),
            user.HasPermission(PermissionKind.EnableAllChannels),
            string.Join(",", user.GetPreferenceValues<Guid>(PreferenceKind.EnabledChannels).Order()),
            user.MaxParentalRatingScore,
            user.MaxParentalRatingSubScore,
            string.Join(",", user.GetPreference(PreferenceKind.BlockUnratedItems).Order(StringComparer.Ordinal)),
            string.Join(",", user.GetPreference(PreferenceKind.BlockedTags).Order(StringComparer.Ordinal)),
            string.Join(",", user.GetPreference(PreferenceKind.AllowedTags).Order(StringComparer.Ordinal)));

    /// <summary>
    /// Searches the library's movies and shows by title, the way the web client's search does.
    /// </summary>
    /// <param name="term">What to search for.</param>
    /// <param name="types">The types to search.</param>
    /// <param name="limit">The most items to return.</param>
    /// <param name="user">Only items this user may see; every item when null.</param>
    /// <returns>The matching movies and shows, best matches first.</returns>
    public IReadOnlyList<BaseItem> Search(string term, IReadOnlyCollection<RequestType> types, int limit, User? user = null)
    {
        ArgumentNullException.ThrowIfNull(types);
        if (string.IsNullOrWhiteSpace(term) || types.Count == 0)
        {
            return Array.Empty<BaseItem>();
        }

        var query = Query(user, null);
        query.IncludeItemTypes = types.Select(ToItemKind).ToArray();
        query.SearchTerm = term;
        query.Limit = limit;
        return _libraryManager.GetItemList(query);
    }

    /// <summary>Gets a library item by its id.</summary>
    /// <param name="id">The item's id.</param>
    /// <returns>The item, or null when there's none.</returns>
    public BaseItem? Get(Guid id) => _libraryManager.GetItemById(id);

    /// <summary>
    /// Returns the library movies and shows these people are in.
    /// </summary>
    /// <param name="personIds">The library person ids.</param>
    /// <param name="limit">The most items to return.</param>
    /// <param name="user">Only items this user may see; every item when null.</param>
    /// <returns>The movies and shows.</returns>
    public IReadOnlyList<BaseItem> ItemsWithPeople(Guid[] personIds, int limit, User? user = null)
    {
        var query = Query(user, null);
        query.IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series };
        query.PersonIds = personIds;
        query.Limit = limit;
        return _libraryManager.GetItemList(query);
    }

    // How many of a user's latest episodes are read for each show the Recently Added and watch history reads want: a
    // season added at once, or watched in one sitting, is many episodes of one show.
    private const int EpisodesPerShow = 20;

    // MARK: RecentlyAdded

    /// <summary>
    /// Returns the movies and shows most recently added to the library, newest first, as the web client's Recently Added
    /// rows have them: a show by its newest episode.
    /// </summary>
    /// <param name="types">The types to include.</param>
    /// <param name="limit">The most items to return.</param>
    /// <param name="user">Only items this user may see.</param>
    /// <returns>The movies and shows.</returns>
    public IReadOnlyList<BaseItem> RecentlyAdded(IReadOnlyCollection<RequestType> types, int limit, User user)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(user);
        var found = new List<(BaseItem Item, DateTime Added)>();
        if (types.Contains(RequestType.Movie))
        {
            var movies = Query(user, RequestType.Movie);
            movies.OrderBy = new[] { (ItemSortBy.DateCreated, SortOrder.Descending) };
            movies.Limit = limit;
            found.AddRange(_libraryManager.GetItemList(movies).Select(i => (i, i.DateCreated)));
        }

        if (types.Contains(RequestType.Series))
        {
            var episodes = Query(user, null);
            episodes.IncludeItemTypes = new[] { BaseItemKind.Episode };
            episodes.OrderBy = new[] { (ItemSortBy.DateCreated, SortOrder.Descending) };
            episodes.Limit = limit * EpisodesPerShow;
            foreach (var show in _libraryManager.GetItemList(episodes).OfType<Episode>().Where(e => e.SeriesId != Guid.Empty).GroupBy(e => e.SeriesId))
            {
                if (_libraryManager.GetItemById(show.Key) is Series series)
                {
                    found.Add((series, show.First().DateCreated));
                }
            }
        }

        return found.OrderByDescending(f => f.Added).Take(limit).Select(f => f.Item).ToList();
    }

    // MARK: RecentlyWatched

    /// <summary>
    /// Returns the movies and shows a user watched most recently, newest first, a show by its latest episode watched;
    /// then their favourites, when they've watched fewer than the limit.
    /// </summary>
    /// <param name="types">The types to include.</param>
    /// <param name="limit">The most items to return.</param>
    /// <param name="user">The user.</param>
    /// <returns>The movies and shows.</returns>
    public IReadOnlyList<BaseItem> RecentlyWatched(IReadOnlyCollection<RequestType> types, int limit, User user)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(user);
        var kinds = types.Select(t => t == RequestType.Movie ? BaseItemKind.Movie : BaseItemKind.Episode).ToArray();
        var titles = new List<BaseItem>();
        var seen = new HashSet<Guid>();
        if (kinds.Length == 0)
        {
            return titles;
        }

        var played = Query(user, null);
        played.IncludeItemTypes = kinds;
        played.IsPlayed = true;
        played.OrderBy = new[] { (ItemSortBy.DatePlayed, SortOrder.Descending) };
        played.Limit = limit * EpisodesPerShow;
        foreach (var item in _libraryManager.GetItemList(played))
        {
            if ((item is Episode episode ? episode.Series : item) is { } title && KindOf(title).HasValue && seen.Add(title.Id))
            {
                titles.Add(title);
            }

            if (titles.Count >= limit)
            {
                return titles;
            }
        }

        var favourites = Query(user, null);
        favourites.IncludeItemTypes = types.Select(ToItemKind).ToArray();
        favourites.IsFavorite = true;
        favourites.OrderBy = new[] { (ItemSortBy.DatePlayed, SortOrder.Descending) };
        favourites.Limit = limit;
        titles.AddRange(_libraryManager.GetItemList(favourites).Where(i => seen.Add(i.Id)).Take(limit - titles.Count));
        return titles;
    }

    /// <summary>
    /// Returns whether a person is in a movie, show or episode in the library that a user may see, so Jellyfin has a
    /// person page for them that the user can open.
    /// </summary>
    /// <param name="personId">The person's library id.</param>
    /// <param name="user">The user; anyone's when null.</param>
    /// <returns>True when they are.</returns>
    public bool IsSeenPerson(Guid personId, User? user)
    {
        var key = (user is null ? "-" : SightKey(user)) + "|" + personId.ToString("N", CultureInfo.InvariantCulture);
        if (_seenPeople.TryGetValue(key, out var kept) && DateTime.UtcNow - kept.AtUtc < FoundFor)
        {
            return kept.Seen;
        }

        if (_seenPeople.Count >= 20000)
        {
            _seenPeople.Clear();
        }

        var seen = SeesPerson(personId, user);
        _seenPeople[key] = (DateTime.UtcNow, seen);
        return seen;
    }

    private bool SeesPerson(Guid personId, User? user)
    {
        // One movie, show or episode they're in that the user may see. A query for a user is already narrowed by the
        // database to what they may see, as the web client's are: to the libraries they may open (their views), and by
        // their parental rating (inherited from the show and the library), the unrated items they're kept from, and
        // their blocked and allowed tags. So the first found settles it, with nothing to check again.
        var query = user is null ? new InternalItemsQuery() : new InternalItemsQuery(user);
        query.IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series, BaseItemKind.Episode };
        query.IsVirtualItem = false;
        query.Recursive = true;
        query.PersonIds = new[] { personId };
        query.Limit = 1;
        return _libraryManager.GetItemList(query).Count > 0;
    }

    // Movies or shows that are really there; with a user, only what they may see, as the web client shows them.
    private static InternalItemsQuery Query(User? user, RequestType? type)
    {
        var query = user is null ? new InternalItemsQuery() : new InternalItemsQuery(user);
        if (type is { } k)
        {
            query.IncludeItemTypes = new[] { ToItemKind(k) };
        }

        query.IsVirtualItem = false;
        query.Recursive = true;
        return query;
    }

    /// <summary>
    /// Returns whether a library item is this title: the same name or original title, ignoring case and
    /// punctuation, and the same year.
    /// </summary>
    /// <param name="item">The library item.</param>
    /// <param name="title">The title.</param>
    /// <param name="year">The year.</param>
    /// <returns>True when it is.</returns>
    public static bool SameTitle(BaseItem item, string title, int year)
    {
        ArgumentNullException.ThrowIfNull(item);
        var itemYear = item.ProductionYear ?? item.PremiereDate?.Year;
        if (itemYear != year)
        {
            return false;
        }

        var wanted = NormalizeTitle(title);
        return wanted.Length > 0 && (NormalizeTitle(StripFileTags(item.Name)) == wanted || NormalizeTitle(item.OriginalTitle) == wanted);
    }

    /// <summary>
    /// Drops what a file or folder name carries beyond the title, so an item Jellyfin has not fetched metadata
    /// for yet, still named "The Matrix (1999) [tmdbid-603]", compares as "The Matrix".
    /// </summary>
    /// <param name="name">The item name.</param>
    /// <returns>The name without bracketed tags or a trailing year.</returns>
    public static string StripFileTags(string? name)
        => FileTags().Replace(TrailingYear().Replace(FileTags().Replace(name ?? string.Empty, " "), string.Empty), " ").Trim();

    [System.Text.RegularExpressions.GeneratedRegex(@"\[[^\]]*\]|\{[^}]*\}")]
    private static partial System.Text.RegularExpressions.Regex FileTags();

    [System.Text.RegularExpressions.GeneratedRegex(@"\s*\((19|20)\d{2}\)\s*$")]
    private static partial System.Text.RegularExpressions.Regex TrailingYear();

    /// <summary>
    /// Reduces a title to lower-case letters and digits, so "Spider-Man: Into the Spider-Verse" and
    /// "Spider Man Into The Spider Verse" compare equal.
    /// </summary>
    /// <param name="title">The title.</param>
    /// <returns>The comparable form.</returns>
    public static string NormalizeTitle(string? title)
        => string.Concat((title ?? string.Empty).Normalize(System.Text.NormalizationForm.FormKD).Where(char.IsLetterOrDigit)).ToLowerInvariant();

    /// <summary>
    /// Reduces text to lower-case words of letters and digits separated by single spaces, for matching whole words.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The words.</returns>
    public static string NormalizeWords(string? text)
        => string.Join(' ', new string((text ?? string.Empty).Normalize(System.Text.NormalizationForm.FormKD)
                .Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c) ? ' ' : '\0')
                .Where(c => c != '\0').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// Counts a show's episodes in the library by season, and notes the seasons the guide says are missing
    /// episodes (Jellyfin's placeholders for episodes it knows of but has no file for).
    /// </summary>
    /// <param name="series">The library series.</param>
    /// <returns>The counts.</returns>
    public SeasonCounts CountEpisodes(BaseItem series)
    {
        ArgumentNullException.ThrowIfNull(series);
        // Kept until the show, one of its seasons or one of its episodes changes; no one changes what's returned.
        if (_counts.TryGetValue(series.Id, out var kept) && DateTime.UtcNow - kept.AtUtc < CountedFor)
        {
            return kept.Counts;
        }

        var counted = Count(series);
        if (_counts.Count >= 5000)
        {
            _counts.Clear();
        }

        _counts[series.Id] = (DateTime.UtcNow, counted);
        return counted;
    }

    /// <summary>Returns the numbers of a season's episodes the library has of a show, a double episode counting for each.</summary>
    /// <param name="series">The show in the library.</param>
    /// <param name="season">The season's number.</param>
    /// <returns>The episode numbers.</returns>
    public HashSet<int> EpisodeNumbers(BaseItem series, int season)
    {
        ArgumentNullException.ThrowIfNull(series);
        var numbers = new HashSet<int>();
        foreach (var episode in _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Episode },
            AncestorIds = new[] { series.Id },
            ParentIndexNumber = season,
            IsVirtualItem = false,
            Recursive = true
        }))
        {
            if (episode.IndexNumber is { } first)
            {
                var last = Math.Clamp((episode as Episode)?.IndexNumberEnd ?? first, first, first + 10);
                for (var n = first; n <= last; n++)
                {
                    numbers.Add(n);
                }
            }
        }

        return numbers;
    }

    private SeasonCounts Count(BaseItem series)
        => Count(_libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Episode },
            AncestorIds = new[] { series.Id },
            Recursive = true
        }));

    /// <summary>
    /// Counts a show's episodes by season. A file holding more than one episode counts for each, as Sonarr counts
    /// them, and an episode in two files counts once.
    /// </summary>
    /// <param name="episodes">The show's episodes in the library, missing ones included.</param>
    /// <returns>The counts.</returns>
    public static SeasonCounts Count(IEnumerable<BaseItem> episodes)
    {
        ArgumentNullException.ThrowIfNull(episodes);
        var counts = new SeasonCounts();
        var numbers = new Dictionary<int, HashSet<int>>();
        var unnumbered = new Dictionary<int, int>();
        foreach (var episode in episodes)
        {
            if (episode.ParentIndexNumber is not { } season)
            {
                continue;
            }

            if (episode.IsVirtualItem)
            {
                counts.Missing.Add(season);
            }
            else if (episode.IndexNumber is { } first)
            {
                var last = Math.Clamp((episode as Episode)?.IndexNumberEnd ?? first, first, first + 10);
                var seen = numbers.TryGetValue(season, out var set) ? set : numbers[season] = new HashSet<int>();
                for (var n = first; n <= last; n++)
                {
                    seen.Add(n);
                    if (season == 0)
                    {
                        counts.Specials.Add(n);
                    }
                }

                if (season == 0 && episode.PremiereDate is { } aired)
                {
                    counts.SpecialDates.Add(aired.Date);
                }
            }
            else
            {
                unnumbered[season] = unnumbered.GetValueOrDefault(season) + 1;
            }
        }

        foreach (var season in numbers.Keys.Union(unnumbered.Keys))
        {
            counts.Have[season] = (numbers.TryGetValue(season, out var set) ? set.Count : 0) + unnumbered.GetValueOrDefault(season);
        }

        return counts;
    }

    /// <summary>
    /// Returns the seasons of a show the library has all of, in order, specials left out: every episode
    /// Sonarr expects when it said how many, none the guide marks missing, and at least one.
    /// </summary>
    /// <param name="counts">The show's episodes in the library.</param>
    /// <param name="expected">Episodes per season as Sonarr reports them, when known.</param>
    /// <returns>The complete season numbers.</returns>
    public static List<int> CompleteSeasons(SeasonCounts counts, IReadOnlyDictionary<int, int>? expected)
    {
        ArgumentNullException.ThrowIfNull(counts);
        return counts.Have
            .Where(p => p.Key > 0 && p.Value > 0 && !counts.Missing.Contains(p.Key)
                && !(expected is not null && expected.TryGetValue(p.Key, out var want) && p.Value < want))
            .Select(p => p.Key)
            .Order()
            .ToList();
    }

    /// <summary>
    /// Returns the seasons of a show the library has in full, as <see cref="CompleteSeasons"/> decides.
    /// </summary>
    /// <param name="series">The library series.</param>
    /// <param name="expected">Episodes per season as Sonarr reports them, when known.</param>
    /// <returns>The complete season numbers.</returns>
    public List<int> SeasonsInLibrary(BaseItem series, IReadOnlyDictionary<int, int>? expected = null)
        => CompleteSeasons(CountEpisodes(series), expected);

    /// <summary>
    /// Returns the movie or show a library change belongs to: the item itself for a movie or series, the
    /// series for a season or episode, and null for anything else.
    /// </summary>
    /// <param name="item">The changed item.</param>
    /// <returns>The movie or series, or null.</returns>
    public static BaseItem? Requestable(BaseItem item) => item switch
    {
        Movie or Series => item,
        Episode episode => episode.Series,
        Season season => season.Series,
        _ => null
    };

    /// <summary>
    /// Returns the request type a library item counts as, or null for any other item.
    /// </summary>
    /// <param name="item">The library item.</param>
    /// <returns>The type, or null.</returns>
    public static RequestType? KindOf(BaseItem item) => item switch
    {
        Movie => RequestType.Movie,
        Series => RequestType.Series,
        _ => null
    };

    private static BaseItemKind ToItemKind(RequestType type) => type switch
    {
        RequestType.Movie => BaseItemKind.Movie,
        RequestType.Series => BaseItemKind.Series,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Only movies and shows can be requested.")
    };
}

/// <summary>
/// A show's episodes in the library, by season.
/// </summary>
public sealed class SeasonCounts
{
    /// <summary>Gets the number of episodes with a file, by season number.</summary>
    public Dictionary<int, int> Have { get; } = new();

    /// <summary>Gets the seasons the guide lists episodes for that have no file.</summary>
    public HashSet<int> Missing { get; } = new();

    /// <summary>Gets the numbers of the specials (season 0's episodes) with a file.</summary>
    public HashSet<int> Specials { get; } = new();

    /// <summary>Gets the days the specials with a file first aired.</summary>
    public HashSet<DateTime> SpecialDates { get; } = new();

    /// <summary>
    /// Gets whether the library has a special: one of its number, or one that aired the day it did, as the library
    /// may number a show's specials by another guide than the one it was requested from.
    /// </summary>
    /// <param name="special">The special.</param>
    /// <returns>True when it's there.</returns>
    public bool HasSpecial(SpecialEpisode special)
    {
        ArgumentNullException.ThrowIfNull(special);
        return Specials.Contains(special.Number) || (special.AirDate is { } aired && SpecialDates.Contains(aired.Date));
    }
}
