using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Necto.Services.Requests;

/// <summary>
/// Cast, crew and people for the details and person pages. Titles in the library use what Jellyfin already
/// knows; anything else is looked up on TMDb (see <see cref="TmdbDiscovery"/>). Lookups are cached for a few hours.
/// </summary>
[PluginService(ServiceLifetime.Singleton)]
public partial class DetailsService
{
    private const int MaxCast = 24;
    private const int MaxCached = 500;
    private static readonly TimeSpan _cacheFor = TimeSpan.FromHours(6);

    // A lookup that found nothing, likely TMDb not answering, is only kept briefly.
    private static readonly TimeSpan _cacheEmptyFor = TimeSpan.FromMinutes(10);

    private readonly ILibraryManager _library;
    private readonly LibraryMatcher _matcher;
    private readonly TmdbDiscovery _tmdb;
    private readonly ConcurrentDictionary<string, (DateTime Expires, object Value)> _cache = new(StringComparer.Ordinal);

    // Library titles' cast and crew as last read, by item: each person looked up by name, so a details page opened
    // again doesn't look them all up again. Forgotten when the item changes or goes; a person's own photo turning up
    // is caught up with within the hour.
    private readonly ConcurrentDictionary<Guid, (DateTime Expires, TitleDetails Details)> _fromLibrary = new();
    private static readonly TimeSpan _libraryFor = TimeSpan.FromHours(1);

    /// <summary>
    /// Initializes a new instance of the <see cref="DetailsService"/> class.
    /// </summary>
    /// <param name="library">The library manager.</param>
    /// <param name="matcher">The library matcher.</param>
    /// <param name="tmdb">TMDb, for titles and people outside the library.</param>
    public DetailsService(ILibraryManager library, LibraryMatcher matcher, TmdbDiscovery tmdb)
    {
        ArgumentNullException.ThrowIfNull(library);
        _tmdb = tmdb;
        _library = library;
        _matcher = matcher;
        _library.ItemUpdated += (_, e) => _fromLibrary.TryRemove(e.Item.Id, out var _);
        _library.ItemRemoved += (_, e) => _fromLibrary.TryRemove(e.Item.Id, out var _);
    }

    /// <summary>
    /// Returns a title's tagline, cast and crew.
    /// </summary>
    /// <param name="lookup">The title.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="user">The viewer; a library item they may not see is not used.</param>
    /// <returns>The details; empty when nothing is known.</returns>
    public async Task<TitleDetails> GetTitleAsync(DetailsLookup lookup, CancellationToken cancellationToken, User? user = null)
    {
        ArgumentNullException.ThrowIfNull(lookup);

        // The library's own record first: it is local, and it is what the viewer will actually watch.
        if (lookup.LibraryItemId is { } id && _library.GetItemById(id) is { } owned && (user is null || LibraryMatcher.IsVisibleTo(owned, user))
            && FromLibrary(owned) is { Cast.Count: > 0 } local)
        {
            // A copy: the kept details are shared, and callers fill in their own parts.
            return local.WithPeople(c => c);
        }

        var ids = ProviderIds.Clean(lookup.ProviderIds);
        if (ids.Count == 0)
        {
            return new TitleDetails();
        }

        // From TMDb in one request, by the title's TMDb id (or a show's TVDB id).
        var key = "title:" + lookup.Type + ":" + string.Join(",", ids.OrderBy(i => i.Key, StringComparer.Ordinal).Select(i => i.Key + "=" + i.Value));
        var found = await Cached(
            key,
            async () => FromTmdb(await _tmdb.TitleDetailsAsync(lookup.Type, ids, cancellationToken).ConfigureAwait(false)) ?? new TitleDetails(),
            d => d.Cast.Count == 0 && d.Tagline is null).ConfigureAwait(false);
        // A copy either way: the cached details are shared, and callers fill in their own parts.
        return found.WithPeople(c => user is null ? c : SeenBy(c, user));
    }

    // A cast or crew member the library has (their id, or a photo from the library) is said to be there only for
    // someone in something the viewer may see; otherwise the person is listed without either, as people search does.
    private PersonCredit SeenBy(PersonCredit credit, User user)
    {
        var match = LibraryPersonUrl().Match(credit.ImageUrl ?? string.Empty);
        var personId = credit.LibraryPersonId ?? (match.Success && Guid.TryParse(match.Groups[1].Value, out var fromImage) ? fromImage : null);
        if (personId is not { } id || _matcher.IsSeenPerson(id, user))
        {
            return credit;
        }

        return new PersonCredit { Name = credit.Name, Role = credit.Role, TmdbId = credit.TmdbId };
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^\.\./Items/([0-9a-fA-F]{32})/")]
    private static partial System.Text.RegularExpressions.Regex LibraryPersonUrl();

    /// <summary>
    /// Finds people by name: those in the library first, then TMDb's.
    /// </summary>
    /// <param name="query">The name to search for.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="user">Who is searching, so only people in what they may see are listed from the library.</param>
    /// <returns>Up to a dozen people.</returns>
    public async Task<List<PersonCredit>> SearchPeopleAsync(string query, CancellationToken cancellationToken, User? user = null)
    {
        query = (query ?? string.Empty).Trim();
        if (query.Length < 2)
        {
            return new List<PersonCredit>();
        }

        // TMDb asked while the library is looked through.
        var fromTmdb = _tmdb.SearchPeopleAsync(query, cancellationToken);

        // People in the library, only those in something the viewer may see.
        var results = _library.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Person },
                SearchTerm = query,
                Limit = 30
            })
            .Where(p => user is null || _matcher.IsSeenPerson(p.Id, user))
            .Take(6)
            .Select(p => new PersonCredit { Name = p.Name, ImageUrl = LibraryImage(p, 320), TmdbId = Tmdb(p.ProviderIds), LibraryPersonId = p.Id })
            .ToList();

        // The rest from TMDb in one request.
        foreach (var r in await fromTmdb.ConfigureAwait(false) ?? new List<TmdbCredit>())
        {
            if (!results.Any(p => (r.TmdbId is not null && p.TmdbId == r.TmdbId) || string.Equals(p.Name, r.Name, StringComparison.OrdinalIgnoreCase)))
            {
                results.Add(new PersonCredit { Name = r.Name.Trim(), ImageUrl = PersonImage(r.ImageUrl), TmdbId = r.TmdbId });
            }
        }

        return results.Take(12).ToList();
    }

    /// <summary>
    /// Returns a person's page: biography and photo, and their movies and shows in the library.
    /// </summary>
    /// <param name="lookup">The person.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="user">The viewer; only library titles they may see are listed.</param>
    /// <returns>The person.</returns>
    public async Task<PersonDetails> GetPersonAsync(PersonLookup lookup, CancellationToken cancellationToken, User? user = null)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        var name = (lookup.Name ?? string.Empty).Trim();
        var details = new PersonDetails { Name = name, TmdbId = lookup.TmdbId, ImageUrl = PersonImage(lookup.ImageUrl) };

        // The library's record of the person only when they're in something the viewer may see.
        var person = string.IsNullOrEmpty(name) ? null : _library.GetPerson(name);
        var titles = person is null ? null : _matcher.ItemsWithPeople(new[] { person.Id }, 200, user);
        // The library's person of that name only when they're the same person: a different TMDb id is someone else.
        if (person is not null && lookup.TmdbId is { } asked && Tmdb(person.ProviderIds) is { } theirs && !string.Equals(asked, theirs, StringComparison.Ordinal))
        {
            person = null;
            titles = null;
        }

        if (person is not null && _matcher.IsSeenPerson(person.Id, user))
        {
            // Jellyfin has a page for them, which the request page opens instead.
            details.LibraryPersonId = person.Id;
        }

        if (person is not null && titles is { Count: > 0 })
        {
            details.Overview = person.Overview;
            details.BirthDate = person.PremiereDate;
            details.DeathDate = person.EndDate;
            details.Birthplace = person.ProductionLocations?.FirstOrDefault();
            details.ImageUrl = LibraryImage(person, 600) ?? details.ImageUrl;
            details.TmdbId ??= Tmdb(person.ProviderIds);
            details.LibraryTitles = titles
                .Select(i => (Item: i, Type: LibraryMatcher.KindOf(i)))
                .Where(x => x.Type.HasValue)
                .Select(x => SearchService.FromLibrary(x.Item, x.Type!.Value))
                .OrderByDescending(r => r.Year ?? 0)
                .ToList();
        }

        // What the library lacks (a biography, mostly) from TMDb, and everything else they're in.
        if (details.TmdbId is { } tmdb && await _tmdb.PersonAsync(tmdb, cancellationToken).ConfigureAwait(false) is { } remote)
        {
            details.Credits = remote.Credits;
            details.Overview ??= remote.Overview;
            details.BirthDate ??= remote.BirthDate;
            details.DeathDate ??= remote.DeathDate;
            details.Birthplace ??= remote.Birthplace;
            details.ImageUrl ??= PersonImage(remote.ImageUrl);
            details.Name = string.IsNullOrEmpty(details.Name) ? remote.Name : details.Name;
        }

        return details;
    }

    // ------------------------------------------------------------------ the library

    private TitleDetails FromLibrary(BaseItem item)
    {
        var now = DateTime.UtcNow;
        if (_fromLibrary.TryGetValue(item.Id, out var kept) && kept.Expires > now)
        {
            return kept.Details;
        }

        if (_fromLibrary.Count >= MaxCached * 4)
        {
            _fromLibrary.Clear();
        }

        var read = ReadLibrary(item);
        _fromLibrary[item.Id] = (now.Add(_libraryFor), read);
        return read;
    }

    private TitleDetails ReadLibrary(BaseItem item)
    {
        var people = _library.GetPeople(item);
        return new TitleDetails
        {
            Name = item.Name ?? string.Empty,
            Tagline = item.Tagline,
            Cast = people.Where(p => p.Type is PersonKind.Actor or PersonKind.GuestStar).Take(MaxCast).Select(Credit).ToList(),
            Directors = people.Where(p => p.Type == PersonKind.Director).Select(Credit).ToList(),
            Writers = people.Where(p => p.Type == PersonKind.Writer).Select(Credit).ToList(),
            Studios = item.Studios?.ToList() ?? new List<string>(),
            Genres = item.Genres?.ToList() ?? new List<string>(),
            OfficialRating = string.IsNullOrEmpty(item.OfficialRating) ? null : item.OfficialRating,
            ReleaseDate = item.PremiereDate,
            CommunityRating = item.CommunityRating is > 0 and var rating ? Math.Round(rating, 1, MidpointRounding.AwayFromZero) : null,
            EndYear = item is Series { Status: SeriesStatus.Ended } && item.EndDate is { } end ? end.Year : null
        };
    }

    // A title's details from TMDb, its people matched to the library.
    private TitleDetails? FromTmdb(TmdbTitle? title)
        => title is null ? null : new TitleDetails
        {
            Name = title.Name,
            Tagline = title.Tagline,
            Cast = title.Cast.Take(MaxCast).Select(Credit).ToList(),
            Directors = title.Directors.Take(4).Select(Credit).ToList(),
            Writers = title.Writers.Take(6).Select(Credit).ToList(),
            Studios = title.Studios.Take(4).ToList(),
            Genres = title.Genres,
            OfficialRating = title.Certification,
            ReleaseDate = title.ReleaseDate,
            CommunityRating = title.Rating is { } rating ? Math.Round(rating, 1, MidpointRounding.AwayFromZero) : null,
            EndYear = title.EndYear,
            TrailerUrl = title.TrailerUrl,
            Collection = title.Collection,
            Traits = title.Traits
        };

    // A TMDb credit, with the library's photo and Jellyfin's id for the person when the library has them: the same
    // person, by TMDb id where both know it.
    private PersonCredit Credit(TmdbCredit c)
    {
        var person = _library.GetPerson(c.Name);
        if (person is not null && c.TmdbId is { } theirs && Tmdb(person.ProviderIds) is { } ours && !string.Equals(theirs, ours, StringComparison.Ordinal))
        {
            person = null;
        }

        return new PersonCredit
        {
            Name = c.Name,
            Role = c.Role,
            ImageUrl = (person is null ? null : LibraryImage(person, 320)) ?? PersonImage(c.ImageUrl),
            TmdbId = c.TmdbId ?? (person is null ? null : Tmdb(person.ProviderIds)),
            LibraryPersonId = person?.Id
        };
    }

    // A library person's photo comes from the server; otherwise the provider's address, if it gave one.
    private PersonCredit Credit(PersonInfo p)
    {
        var person = _library.GetPerson(p.Name);
        return new PersonCredit
        {
            Name = p.Name,
            Role = string.IsNullOrWhiteSpace(p.Role) ? null : p.Role,
            ImageUrl = (person is null ? null : LibraryImage(person, 320)) ?? PersonImage(p.ImageUrl),
            TmdbId = Tmdb(p.ProviderIds) ?? (person is null ? null : Tmdb(person.ProviderIds)),
            LibraryPersonId = person?.Id
        };
    }

    // ------------------------------------------------------------------ helpers

    // At most MaxCached entries: past that, expired ones go first, then the ones closest to expiring.
    private async Task<T> Cached<T>(string key, Func<Task<T>> load, Func<T, bool> isEmpty)
        where T : class
    {
        if (_cache.TryGetValue(key, out var hit) && hit.Expires > DateTime.UtcNow && hit.Value is T value)
        {
            return value;
        }

        var loaded = await load().ConfigureAwait(false);
        _cache[key] = (DateTime.UtcNow.Add(isEmpty(loaded) ? _cacheEmptyFor : _cacheFor), loaded);
        if (_cache.Count > MaxCached)
        {
            var now = DateTime.UtcNow;
            foreach (var old in _cache.OrderBy(e => e.Value.Expires).Take(_cache.Count - MaxCached + (MaxCached / 10)).Where(e => e.Key != key || e.Value.Expires <= now).Select(e => e.Key).ToList())
            {
                _cache.TryRemove(old, out _);
            }
        }

        return loaded;
    }

    private static string? Tmdb(Dictionary<string, string>? ids)
        => ids is not null && ids.TryGetValue(MetadataProvider.Tmdb.ToString(), out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

    private static string? LibraryImage(BaseItem item, int height)
        => item.HasImage(ImageType.Primary)
            ? "../Items/" + item.Id.ToString("N", CultureInfo.InvariantCulture) + "/Images/Primary?fillHeight=" + height.ToString(CultureInfo.InvariantCulture) + "&quality=90"
            : null;

    // Only from the artwork hosts posters may come from, as a person's photo address can come from the viewer's own
    // request; TMDb profile photos are served small, since their originals are megabytes.
    internal static string? PersonImage(string? url) => RequestService.SafePersonUrl(url);
}
