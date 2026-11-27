using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests;

/// <summary>
/// Every movie and show in the library by its provider ids and by its title and year, held in memory, so a list of
/// titles from outside the library is matched to it without asking the database about each one. Read once in the
/// background the first time it's needed, then kept as the library changes. Who may see an item is still decided for
/// each viewer, as the web client decides it.
/// </summary>
public partial class LibraryMatcher
{
    private readonly object _indexLock = new();
    private readonly Dictionary<Guid, IndexEntry> _index = new();
    private readonly Dictionary<string, HashSet<Guid>> _byKey = new(StringComparer.Ordinal);

    // Removed while the library was being read, so the read doesn't bring them back.
    private readonly HashSet<Guid> _goneWhileReading = new();

    // 0 not read, 1 being read, 2 ready.
    private int _indexState;

    private sealed record IndexEntry(string Shape, string[] Keys);

    /// <summary>Gets a value indicating whether the library has been read into memory.</summary>
    internal bool Indexed => Volatile.Read(ref _indexState) == 2;

    // What a match or who may see an item depends on: its names, year, ids, ratings, tags and where it is. An update
    // that changes none of it (as scans, image and metadata refreshes mostly are) changes nothing kept.
    private static string Shape(BaseItem item)
        => string.Join(
            "|",
            item.Name,
            item.OriginalTitle,
            item.ProductionYear,
            item.PremiereDate?.Year,
            item.OfficialRating,
            item.CustomRating,
            item.InheritedParentalRatingValue,
            string.Join(",", item.Tags ?? Array.Empty<string>()),
            item.ParentId,
            string.Join(",", item.ProviderIds.OrderBy(i => i.Key, StringComparer.Ordinal).Select(i => i.Key + "=" + i.Value)));

    private static string IdKey(RequestType type, string key, string value)
        => type + "|" + key.ToUpperInvariant() + "=" + value.ToUpperInvariant();

    private static string TitleKey(RequestType type, string normalizedTitle, int year)
        => type + "|t=" + normalizedTitle + "|" + year.ToString(System.Globalization.CultureInfo.InvariantCulture);

    // The keys an item is found by: each of its ids, and its name and original title with its year, as SameTitle compares them.
    private static string[] KeysOf(BaseItem item, RequestType type)
    {
        var keys = ProviderIds.Clean(item.ProviderIds).Select(i => IdKey(type, i.Key, i.Value)).ToList();
        if ((item.ProductionYear ?? item.PremiereDate?.Year) is { } year)
        {
            foreach (var title in new[] { NormalizeTitle(StripFileTags(item.Name)), NormalizeTitle(item.OriginalTitle) }.Where(t => t.Length > 0).Distinct(StringComparer.Ordinal))
            {
                keys.Add(TitleKey(type, title, year));
            }
        }

        return keys.ToArray();
    }

    // Keeps an item as it is now. Returns whether anything a match depends on changed (or it wasn't known).
    private bool IndexItem(BaseItem item)
    {
        if (item.IsVirtualItem || KindOf(item) is not { } type)
        {
            return UnindexItem(item.Id);
        }

        // Read under the lock, so two updates to one item at once can't keep the older.
        lock (_indexLock)
        {
            var shape = Shape(item);
            if (_index.TryGetValue(item.Id, out var was) && string.Equals(was.Shape, shape, StringComparison.Ordinal))
            {
                return false;
            }

            Drop(item.Id, was);
            Add(item.Id, new IndexEntry(shape, KeysOf(item, type)));
            return true;
        }
    }

    private bool UnindexItem(Guid id)
    {
        lock (_indexLock)
        {
            if (_indexState == 1)
            {
                _goneWhileReading.Add(id);
            }

            if (!_index.TryGetValue(id, out var was))
            {
                return false;
            }

            Drop(id, was);
            return true;
        }
    }

    // Under _indexLock.
    private void Add(Guid id, IndexEntry entry)
    {
        _index[id] = entry;
        foreach (var key in entry.Keys)
        {
            if (!_byKey.TryGetValue(key, out var ids))
            {
                _byKey[key] = ids = new HashSet<Guid>();
            }

            ids.Add(id);
        }
    }

    // Under _indexLock.
    private void Drop(Guid id, IndexEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        _index.Remove(id);
        foreach (var key in entry.Keys)
        {
            if (_byKey.TryGetValue(key, out var ids) && ids.Remove(id) && ids.Count == 0)
            {
                _byKey.Remove(key);
            }
        }
    }

    // When reading the library last failed, so it isn't read again on every lookup.
    private long _indexFailedTicks;
    private static readonly TimeSpan IndexRetryAfter = TimeSpan.FromMinutes(5);

    /// <summary>Starts reading the library into memory, unless it has been or is being read.</summary>
    internal void WarmIndex() => EnsureIndex();

    private void EnsureIndex()
    {
        if (DateTime.UtcNow.Ticks - Interlocked.Read(ref _indexFailedTicks) < IndexRetryAfter.Ticks)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _indexState, 1, 0) == 0)
        {
            _ = Task.Run(ReadIndex);
        }
    }

    // Reads every movie and show once. What changed while it was being read is already kept as it is now, so the read
    // adds only the rest, and nothing removed meanwhile.
    private void ReadIndex()
    {
        try
        {
            var items = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
                Recursive = true,
                IsVirtualItem = false
            });
            var read = items.Select(i => (i.Id, Type: KindOf(i), Item: i)).Where(i => i.Type is not null)
                .Select(i => (i.Id, Entry: new IndexEntry(Shape(i.Item), KeysOf(i.Item, i.Type!.Value)))).ToList();
            lock (_indexLock)
            {
                foreach (var (id, entry) in read)
                {
                    if (!_index.ContainsKey(id) && !_goneWhileReading.Contains(id))
                    {
                        Add(id, entry);
                    }
                }

                _goneWhileReading.Clear();
                _indexState = 2;
            }
        }
        catch (Exception ex)
        {
            // Matched through the database meanwhile; read again the next time it's needed, after a while.
            _logger?.LogWarning(ex, "Reading the library to match titles against failed; titles are matched through the database until it can be read");
            Interlocked.Exchange(ref _indexFailedTicks, DateTime.UtcNow.Ticks);
            lock (_indexLock)
            {
                _goneWhileReading.Clear();
                _indexState = 0;
            }
        }
    }

    // The items kept under any of these keys, or null while the library hasn't been read.
    private List<Guid>? Lookup(IEnumerable<string> keys)
    {
        if (!Indexed)
        {
            EnsureIndex();
            return null;
        }

        lock (_indexLock)
        {
            return keys.SelectMany(k => _byKey.TryGetValue(k, out var ids) ? ids : Enumerable.Empty<Guid>()).Distinct().ToList();
        }
    }

    // Find from memory: not answered when the library hasn't been read (the caller asks the database), else the answer,
    // which may be no item.
    private (BaseItem? Item, bool Answered) FindIndexed(RequestType type, Dictionary<string, string> clean, string? title, int? year, User? user)
    {
        if (Lookup(clean.Select(i => IdKey(type, i.Key, i.Value))) is not { } byId)
        {
            return (null, false);
        }

        BaseItem? First(IEnumerable<Guid> ids, Func<BaseItem, bool> matches)
            => ids.Select(_libraryManager.GetItemById)
                .FirstOrDefault(i => i is not null && !i.IsVirtualItem && KindOf(i) == type && matches(i) && (user is null || IsVisibleTo(i, user)));

        if (First(byId, _ => true) is { } found)
        {
            return (found, true);
        }

        if (string.IsNullOrWhiteSpace(title) || year is not { } y || NormalizeTitle(title) is not { Length: > 0 } wanted)
        {
            return (null, true);
        }

        return (First(Lookup(new[] { TitleKey(type, wanted, y) }) ?? new List<Guid>(), i => IsTitleMatch(i, clean, title, y)), true);
    }
}
