using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using SearchResult = Jellyfin.Plugin.Necto.Services.Requests.Models.SearchResult;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Necto.Services.Requests;

/// <summary>
/// Turns requests into Jellyfin's item format (<see cref="BaseItemDto"/>): the library's own item, built by Jellyfin
/// for the viewer, once the title is there; otherwise the title as requested, marked as not in the library yet.
/// </summary>
[PluginService(ServiceLifetime.Singleton)]
public class RequestItems
{
    // BaseItemDto's properties, copied onto the request's item from the one Jellyfin builds: compiled once into a
    // single method that sets each in turn, rather than read and set through reflection for every item of every list.
    private static readonly Action<BaseItemDto, BaseItemDto> _copyProperties = CompileCopy();

    private readonly IDtoService _dtos;
    private readonly ILibraryManager _library;
    private readonly IServerApplicationHost _host;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestItems"/> class.
    /// </summary>
    /// <param name="dtos">Jellyfin's item builder.</param>
    /// <param name="library">The library.</param>
    /// <param name="host">The server, for its id.</param>
    public RequestItems(IDtoService dtos, ILibraryManager library, IServerApplicationHost host)
    {
        _dtos = dtos;
        _library = library;
        _host = host;
    }

    // What a library title's item carries in a list, as Jellyfin's own lists return by default: its overview, genres,
    // ids and one image of each kind, with the viewer's played state. Not its people, streams, chapters or child
    // counts, which take queries of their own for each title and which no list shows.
    private static DtoOptions ListOptions() => new(false)
    {
        Fields = new[] { ItemFields.Overview, ItemFields.Genres, ItemFields.ProviderIds, ItemFields.PrimaryImageAspectRatio, ItemFields.DateCreated },
        ImageTypeLimit = 1,
        EnableImages = true,
        EnableUserData = true
    };

    /// <summary>
    /// Gets a request as an item. Its library item is used only when it's still set on the request as shown, which
    /// leaves it off for a viewer who can't see it.
    /// </summary>
    /// <param name="shown">The request, as the viewer may see it.</param>
    /// <param name="viewer">Who is looking, for their own view of a library item; null for an API key.</param>
    /// <returns>The item.</returns>
    public RequestItemDto For(MediaRequest shown, Jellyfin.Database.Implementations.Entities.User? viewer)
    {
        ArgumentNullException.ThrowIfNull(shown);
        return Finish(shown, shown.LibraryItemId is { } id && _library.GetItemById(id) is { } owned
            ? Copy(_dtos.GetBaseItemDto(owned, ListOptions(), viewer))
            : null);
    }

    /// <summary>
    /// Gets a page of requests as items, as <see cref="For(MediaRequest, Jellyfin.Database.Implementations.Entities.User?)"/>
    /// gets each, in the same order: the library items among them read together and built by Jellyfin in one go.
    /// </summary>
    /// <param name="shown">The requests, as the viewer may see them.</param>
    /// <param name="viewer">Who is looking; null for an API key.</param>
    /// <param name="loaded">Library items already read for this page, by id, so they aren't read again.</param>
    /// <returns>The items.</returns>
    public List<RequestItemDto> For(IReadOnlyList<MediaRequest> shown, Jellyfin.Database.Implementations.Entities.User? viewer, IReadOnlyDictionary<Guid, BaseItem>? loaded = null)
    {
        ArgumentNullException.ThrowIfNull(shown);
        var built = Build(shown.Select(s => s.LibraryItemId), viewer, loaded);
        return shown.Select(s => Finish(s, s.LibraryItemId is { } id && built.TryGetValue(id, out var dto) ? Copy(dto) : null)).ToList();
    }

    // A request's item finished: the library's own as built, or the title as requested when there's none.
    private RequestItemDto Finish(MediaRequest shown, RequestItemDto? owned)
    {
        var item = owned ?? new RequestItemDto
        {
            Id = shown.Id,
            ServerId = _host.SystemId,
            Name = shown.Title,
            Type = shown.Type == RequestType.Series ? BaseItemKind.Series : BaseItemKind.Movie,
            IsFolder = shown.Type == RequestType.Series,
            MediaType = shown.Type == RequestType.Series ? MediaType.Unknown : MediaType.Video,
            ProductionYear = shown.Year,
            Overview = shown.Overview,
            ProviderIds = shown.ProviderIds.ToDictionary(p => p.Key, p => (string?)p.Value, StringComparer.OrdinalIgnoreCase),
            // Jellyfin's mark for an item it knows of but doesn't have the files of.
            LocationType = LocationType.Virtual,
            // Not in the library yet, so the item came about when it was asked for.
            DateCreated = shown.CreatedUtc
        };
        // How far its download has got, in Jellyfin's own field for how complete an item is (0 to 100), where Jellyfin
        // hasn't set it.
        item.CompletionPercentage ??= shown.Progress is { } progress ? Math.Round(progress * 100, 1) : null;

        // Where the request stands, in the item's own status field where Jellyfin leaves it empty (a movie, or a title
        // not in the library yet), so an app that only knows Jellyfin's items can still tell Pending from Approved. A
        // show's airing status (Continuing or Ended), which Jellyfin sets, is left as it is; Request.Status has it then.
        if (string.IsNullOrEmpty(item.Status))
        {
            item.Status = shown.Status.ToString();
        }

        item.RequestId = shown.Id;
        item.Request = shown;
        return item;
    }

    /// <summary>
    /// Gets a title found by search as an item: the library's own, built for the viewer, when the result names one
    /// (which it does only for one the viewer may see), otherwise the title as found.
    /// </summary>
    /// <param name="result">The result, as the viewer may see it.</param>
    /// <param name="viewer">Who is looking; null for an API key.</param>
    /// <returns>The item.</returns>
    public SearchItemDto For(SearchResult result, Jellyfin.Database.Implementations.Entities.User? viewer)
    {
        ArgumentNullException.ThrowIfNull(result);
        return Finish(result, result.LibraryItemId is { } id && _library.GetItemById(id) is { } owned
            ? Copy<SearchItemDto>(_dtos.GetBaseItemDto(owned, ListOptions(), viewer))
            : null);
    }

    /// <summary>
    /// Gets a page of titles found by search as items, as <see cref="For(SearchResult, Jellyfin.Database.Implementations.Entities.User?)"/>
    /// gets each, in the same order: the library items among them read together and built by Jellyfin in one go.
    /// </summary>
    /// <param name="results">The results, as the viewer may see them.</param>
    /// <param name="viewer">Who is looking; null for an API key.</param>
    /// <returns>The items.</returns>
    public List<SearchItemDto> For(IReadOnlyList<SearchResult> results, Jellyfin.Database.Implementations.Entities.User? viewer)
    {
        ArgumentNullException.ThrowIfNull(results);
        var built = Build(results.Select(r => r.LibraryItemId), viewer, null);
        return results.Select(r => Finish(r, r.LibraryItemId is { } id && built.TryGetValue(id, out var dto) ? Copy<SearchItemDto>(dto) : null)).ToList();
    }

    // A search result's item finished: the library's own as built, or the title as found when there's none.
    private SearchItemDto Finish(SearchResult result, SearchItemDto? owned)
    {
        var item = owned ?? new SearchItemDto
        {
            Id = VirtualId(result.Type == RequestType.Series ? "Series" : "Movie", result.ProviderIds, result.Title + "|" + result.Year),
            ServerId = _host.SystemId,
            Name = result.Title,
            OriginalTitle = result.OriginalTitle,
            Type = result.Type == RequestType.Series ? BaseItemKind.Series : BaseItemKind.Movie,
            IsFolder = result.Type == RequestType.Series,
            MediaType = result.Type == RequestType.Series ? MediaType.Unknown : MediaType.Video,
            ProductionYear = result.Year,
            PremiereDate = result.ReleaseDate,
            Overview = result.Overview,
            Genres = result.Genres.ToArray(),
            OfficialRating = result.Certification,
            CommunityRating = (float?)(result.CommunityRating ?? result.TmdbRating),
            RunTimeTicks = result.RuntimeMinutes is { } minutes ? TimeSpan.FromMinutes(minutes).Ticks : null,
            Status = result.Status,
            ProviderIds = result.ProviderIds.ToDictionary(p => p.Key, p => (string?)p.Value, StringComparer.OrdinalIgnoreCase),
            LocationType = LocationType.Virtual
        };
        item.Result = result;
        return item;
    }

    /// <summary>
    /// Reads library items by id in one query, rather than one at a time. Read for no one in particular, so nothing is
    /// left out here: who may see each is decided after.
    /// </summary>
    /// <param name="ids">The items' ids.</param>
    /// <returns>The items found, by id.</returns>
    public Dictionary<Guid, BaseItem> Load(IEnumerable<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var wanted = ids.Where(id => id != Guid.Empty).Distinct().ToArray();
        var found = new Dictionary<Guid, BaseItem>();
        if (wanted.Length == 0)
        {
            return found;
        }

        foreach (var item in _library.GetItemList(new InternalItemsQuery { ItemIds = wanted }))
        {
            found.TryAdd(item.Id, item);
        }

        return found;
    }

    // The library items named, each built once by Jellyfin for the viewer, by id. Read together (those not already
    // read for the page), and built in one call, which reads everyone's played state for them in one query rather
    // than one each. Those were already decided as ones the viewer may see, so Jellyfin isn't asked to check again.
    private Dictionary<Guid, BaseItemDto> Build(IEnumerable<Guid?> ids, Jellyfin.Database.Implementations.Entities.User? viewer, IReadOnlyDictionary<Guid, BaseItem>? loaded)
    {
        var wanted = ids.OfType<Guid>().Distinct().ToList();
        var built = new Dictionary<Guid, BaseItemDto>();
        if (wanted.Count == 0)
        {
            return built;
        }

        var items = new Dictionary<Guid, BaseItem>();
        foreach (var id in wanted)
        {
            if (loaded is not null && loaded.TryGetValue(id, out var known))
            {
                items[id] = known;
            }
        }

        if (items.Count < wanted.Count)
        {
            foreach (var (id, item) in Load(wanted.Where(id => !items.ContainsKey(id))))
            {
                items[id] = item;
            }
        }

        // Any the query didn't return (gone since, or a library that answers by id only) read on their own.
        foreach (var id in wanted.Where(id => !items.ContainsKey(id)))
        {
            if (_library.GetItemById(id) is { } one)
            {
                items[id] = one;
            }
        }

        var list = items.Values.ToList();
        if (list.Count == 0)
        {
            return built;
        }

        foreach (var dto in _dtos.GetBaseItemDtos(list, ListOptions(), viewer, skipVisibilityCheck: true))
        {
            if (dto is not null && items.ContainsKey(dto.Id))
            {
                built.TryAdd(dto.Id, dto);
            }
        }

        // One Jellyfin's batch left out is built on its own, as a single item is.
        foreach (var item in list.Where(i => !built.ContainsKey(i.Id)))
        {
            built[item.Id] = _dtos.GetBaseItemDto(item, ListOptions(), viewer);
        }

        return built;
    }

    /// <summary>Gets a person found by search as Jellyfin describes a person.</summary>
    /// <param name="person">The person.</param>
    /// <returns>The item.</returns>
    public PersonItemDto For(PersonCredit person)
    {
        ArgumentNullException.ThrowIfNull(person);
        return new PersonItemDto
        {
            Id = VirtualId("Person", person.TmdbId is { Length: > 0 } id ? new Dictionary<string, string> { ["Tmdb"] = id } : new Dictionary<string, string>(), person.Name),
            ServerId = _host.SystemId,
            Name = person.Name,
            Type = BaseItemKind.Person,
            ProviderIds = person.TmdbId is { Length: > 0 } tmdb ? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["Tmdb"] = tmdb } : new Dictionary<string, string?>(),
            LocationType = LocationType.Virtual,
            Person = person
        };
    }

    // The ids a virtual item's id is made from, the first it has.
    private static readonly string[] _idOrder = { "Tmdb", "Tvdb", "Imdb" };

    /// <summary>
    /// Gets the id a title or person outside the library goes by: the same for it in every list, from its type and its
    /// first id of TMDb, TVDB and IMDb (its name when it has none), as a name-based GUID (RFC 4122 version 3) in the
    /// plugin's own namespace, so it can't be a library item's.
    /// </summary>
    /// <param name="kind">Movie, Series or Person.</param>
    /// <param name="ids">Its provider ids.</param>
    /// <param name="name">Its name, for one with no ids.</param>
    /// <returns>The id.</returns>
    internal static Guid VirtualId(string kind, IReadOnlyDictionary<string, string> ids, string? name)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var key = _idOrder
            .Select(p => ids.FirstOrDefault(i => string.Equals(i.Key, p, StringComparison.OrdinalIgnoreCase)))
            .Where(i => !string.IsNullOrWhiteSpace(i.Value))
            .Select(i => i.Key.ToLowerInvariant() + ":" + i.Value.Trim())
            .FirstOrDefault() ?? "name:" + (name ?? string.Empty).Trim().ToLowerInvariant();
#pragma warning disable CA5351 // MD5 is what a version 3 GUID is made with; nothing secret rides on it.
        var hash = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes("jellyfin-plugin-requests/" + kind + "/" + key));
#pragma warning restore CA5351
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash, true);
    }

    private static RequestItemDto Copy(BaseItemDto from) => Copy<RequestItemDto>(from);

    private static T Copy<T>(BaseItemDto from)
        where T : BaseItemDto, new()
    {
        var to = new T();
        _copyProperties(from, to);
        return to;
    }

    // (from, to) => { to.A = from.A; to.B = from.B; ... } over every property BaseItemDto can read and set.
    private static Action<BaseItemDto, BaseItemDto> CompileCopy()
    {
        var from = Expression.Parameter(typeof(BaseItemDto), "from");
        var to = Expression.Parameter(typeof(BaseItemDto), "to");
        var assignments = typeof(BaseItemDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0 && p.GetSetMethod() is not null && p.GetGetMethod() is not null)
            .Select(p => (Expression)Expression.Assign(Expression.Property(to, p), Expression.Property(from, p)))
            .ToList();
        var body = assignments.Count == 0 ? (Expression)Expression.Empty() : Expression.Block(assignments);
        return Expression.Lambda<Action<BaseItemDto, BaseItemDto>>(body, from, to).Compile();
    }
}
