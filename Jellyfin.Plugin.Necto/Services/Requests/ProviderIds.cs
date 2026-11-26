using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests;

/// <summary>
/// The provider ids a request is matched by, and how two sets of them are compared.
/// </summary>
public static class ProviderIds
{
    /// <summary>The TMDb id key.</summary>
    public const string Tmdb = "Tmdb";

    /// <summary>The IMDb id key.</summary>
    public const string Imdb = "Imdb";

    /// <summary>The TVDb id key.</summary>
    public const string Tvdb = "Tvdb";

    private static readonly string[] _keys = { Tmdb, Imdb, Tvdb };

    /// <summary>
    /// Keeps only the ids requests are matched by, trimmed, with empty and oversized values dropped.
    /// </summary>
    /// <param name="ids">The ids to clean.</param>
    /// <returns>The cleaned ids, possibly empty.</returns>
    public static Dictionary<string, string> Clean(IReadOnlyDictionary<string, string>? ids)
    {
        var clean = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (ids is null)
        {
            return clean;
        }

        foreach (var key in _keys)
        {
            var value = ids.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)).Value?.Trim();
            // Only what such an id looks like: digits for TMDb and TVDB, tt and digits for IMDb. They go into the
            // addresses asked of TMDb, Radarr and Sonarr.
            if (!string.IsNullOrEmpty(value) && value.Length <= 32 && IsWellFormed(key, value) && Canonical(key, value) is { } canonical)
            {
                clean[key] = canonical;
            }
        }

        return clean;
    }

    // Whether a value looks like an id of its kind (one of _keys).
    private static bool IsWellFormed(string key, string value)
        => string.Equals(key, Imdb, StringComparison.OrdinalIgnoreCase)
            ? value.Length > 2 && value.StartsWith("tt", StringComparison.OrdinalIgnoreCase) && value[2..].All(char.IsAsciiDigit)
            : value.All(char.IsAsciiDigit);

    // An id as its provider writes it, so the same title is never known by two spellings (603 and 0603), which would
    // slip past the blocklist and the check for a request already made: a number without leading zeros, and IMDb's
    // tt in lower case. Null for a number that is all zeros.
    private static string? Canonical(string key, string value)
    {
        if (string.Equals(key, Imdb, StringComparison.OrdinalIgnoreCase))
        {
            return "tt" + value[2..];
        }

        var number = value.TrimStart('0');
        return number.Length == 0 ? null : number;
    }

    /// <summary>
    /// Returns whether two sets of ids name the same title: any id they both carry is equal.
    /// </summary>
    /// <param name="a">The first set.</param>
    /// <param name="b">The second set.</param>
    /// <returns>True when they share an id.</returns>
    public static bool Overlap(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return _keys.Any(key => a.TryGetValue(key, out var x) && b.TryGetValue(key, out var y) && string.Equals(x, y, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns whether two sets of ids name different titles: they carry the same type of id with different values.
    /// A title match is not trusted then, since remakes and same-named shows share titles and years.
    /// </summary>
    /// <param name="a">The first set.</param>
    /// <param name="b">The second set.</param>
    /// <returns>True when they disagree on an id.</returns>
    public static bool Conflict(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return _keys.Any(key => a.TryGetValue(key, out var x) && b.TryGetValue(key, out var y) && !string.Equals(x, y, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns whether a request is for this title.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="type">The title's type.</param>
    /// <param name="ids">The title's ids.</param>
    /// <returns>True when it is.</returns>
    public static bool Matches(MediaRequest request, RequestType type, IReadOnlyDictionary<string, string> ids)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Type == type && Overlap(request.ProviderIds, ids);
    }
}
