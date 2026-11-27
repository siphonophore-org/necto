using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests;

/// <summary>
/// What may not be requested: titles picked by an administrator, and any title with a blocked genre, age
/// rating, word in its name, TMDb keyword or original language, or in a blocked collection. Administrators may still
/// request blocked titles.
/// </summary>
public static class Blocklist
{
    /// <summary>
    /// Returns why a title is blocked, for administrators, or null when it may be requested.
    /// </summary>
    /// <param name="config">The configuration.</param>
    /// <param name="type">The title's type.</param>
    /// <param name="ids">The title's provider ids.</param>
    /// <param name="title">The title.</param>
    /// <param name="genres">Its genres, when known.</param>
    /// <param name="rating">Its age rating, when known.</param>
    /// <param name="traits">Its TMDb keywords, original language and collection, when known.</param>
    /// <returns>The reason, or null.</returns>
    public static string? Reason(RequestSettings config, RequestType type, IReadOnlyDictionary<string, string> ids, string? title, IEnumerable<string>? genres, string? rating, TitleTraits? traits = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(ids);
        var blocks = Compiled.For(config);
        if (blocks.Titles.Count > 0 && Ids.Any(key => ids.TryGetValue(key, out var value) && value is not null && blocks.Titles.Contains(TitleKey(type, key, value))))
        {
            return "Blocked by an administrator";
        }

        if (blocks.Genres.Count > 0 && genres?.FirstOrDefault(g => g is not null && blocks.Genres.Contains(g)) is { } genre)
        {
            return "The " + genre + " genre is blocked";
        }

        if (!string.IsNullOrWhiteSpace(rating) && blocks.Ratings.Contains(rating.Trim()))
        {
            return rating.Trim() + " titles are blocked";
        }

        if (traits is not null)
        {
            if (traits.Keywords.Count > 0 && blocks.Tags.FirstOrDefault(t => traits.Keywords.Contains(t.Id)) is { } tag)
            {
                return "Titles tagged “" + tag.Name + "” are blocked";
            }

            if (!string.IsNullOrWhiteSpace(traits.Language) && blocks.Languages.TryGetValue(traits.Language, out var language))
            {
                return "Titles in " + LanguageName(language) + " are blocked";
            }

            if (traits.Collection is { } collection && blocks.Collections.TryGetValue(collection, out var set))
            {
                return set.Name + " is blocked";
            }
        }

        if (blocks.Keywords.Length == 0)
        {
            return null;
        }

        var normalized = " " + LibraryMatcher.NormalizeWords(title) + " ";
        foreach (var (keyword, word) in blocks.Keywords)
        {
            if (normalized.Contains(word, StringComparison.Ordinal))
            {
                return "Titles with “" + keyword + "” are blocked";
            }
        }

        return null;
    }

    // The ids a blocked title is matched by, in the order ProviderIds compares them.
    private static readonly string[] Ids = { ProviderIds.Tmdb, ProviderIds.Imdb, ProviderIds.Tvdb };

    // What a blocked title is kept under: its type, the kind of id and the id. Compared ignoring case, as ids are.
    private static string TitleKey(RequestType type, string key, string value) => type + "|" + key + "|" + value;

    // The block lists read into what checks a title quickly, once per configuration rather than once per title: every
    // list and search checks each of its titles, often twice. Kept with the configuration it was read from, and read
    // again when any list has been replaced or has grown or shrunk since, as saving the settings or blocking a title does.
    private sealed class Compiled
    {
        private static readonly ConditionalWeakTable<RequestSettings, Compiled> _kept = new();

        // The lists it was read from, and how long each was then.
        private readonly (object? List, int Count)[] _read;

        private Compiled(RequestSettings config)
        {
            _read = new (object?, int)[]
            {
                Of(config.BlockedTitles), Of(config.BlockedGenres), Of(config.BlockedRatings), Of(config.BlockedKeywords),
                Of(config.BlockedTags), Of(config.BlockedLanguages), Of(config.BlockedCollections)
            };

            Titles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var blocked in (config.BlockedTitles ?? new()).Where(t => t is not null))
            {
                foreach (var (key, value) in IdsOf(blocked))
                {
                    // Clean names each kind as Ids does, so a title's ids are looked up under the same names.
                    Titles.Add(TitleKey(blocked.Type, key, value));
                }
            }

            Genres = new HashSet<string>((config.BlockedGenres ?? new()).Where(g => g is not null), StringComparer.OrdinalIgnoreCase);
            Ratings = new HashSet<string>((config.BlockedRatings ?? new()).Where(r => r is not null), StringComparer.OrdinalIgnoreCase);
            Tags = (config.BlockedTags ?? new()).Where(t => t is not null).ToArray();

            // The first of each, as the lists are read in order.
            Languages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var language in (config.BlockedLanguages ?? new()).Where(l => l is not null))
            {
                Languages.TryAdd(language, language);
            }

            Collections = new Dictionary<int, TmdbName>();
            foreach (var set in (config.BlockedCollections ?? new()).Where(c => c is not null))
            {
                Collections.TryAdd(set.Id, set);
            }

            // Each word as a title's words are compared, spaced either side so only whole words match.
            Keywords = (config.BlockedKeywords ?? new())
                .Select(k => (Keyword: k, Word: LibraryMatcher.NormalizeWords(k)))
                .Where(k => k.Word is { Length: > 0 })
                .Select(k => (k.Keyword, " " + k.Word + " "))
                .ToArray();
        }

        public HashSet<string> Titles { get; }

        public HashSet<string> Genres { get; }

        public HashSet<string> Ratings { get; }

        public TmdbName[] Tags { get; }

        public Dictionary<string, string> Languages { get; }

        public Dictionary<int, TmdbName> Collections { get; }

        public (string Keyword, string Word)[] Keywords { get; }

        public static Compiled For(RequestSettings config)
        {
            if (_kept.TryGetValue(config, out var kept) && kept.IsCurrent(config))
            {
                return kept;
            }

            var compiled = new Compiled(config);
            _kept.AddOrUpdate(config, compiled);
            return compiled;
        }

        private static (object? List, int Count) Of<T>(List<T>? list) => (list, list?.Count ?? -1);

        private static bool Same<T>((object? List, int Count) read, List<T>? list)
            => ReferenceEquals(read.List, list) && read.Count == (list?.Count ?? -1);

        private bool IsCurrent(RequestSettings config)
            => Same(_read[0], config.BlockedTitles) && Same(_read[1], config.BlockedGenres) && Same(_read[2], config.BlockedRatings)
                && Same(_read[3], config.BlockedKeywords) && Same(_read[4], config.BlockedTags) && Same(_read[5], config.BlockedLanguages)
                && Same(_read[6], config.BlockedCollections);
    }

    /// <summary>Gets whether any block needs what only TMDb knows of a title: its keywords, language or collection.</summary>
    /// <param name="config">The configuration.</param>
    /// <returns>True when one does.</returns>
    public static bool NeedsTraits(RequestSettings config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return config.BlockedTags.Count > 0 || config.BlockedLanguages.Count > 0 || config.BlockedCollections.Count > 0;
    }

    // A language's English name, such as Japanese for ja; the code itself when it isn't one.
    private static string LanguageName(string code)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(code);
            return culture.EnglishName.StartsWith("Unknown", StringComparison.Ordinal) ? code : culture.EnglishName;
        }
        catch (CultureNotFoundException)
        {
            return code;
        }
    }

    /// <summary>
    /// Returns whether a blocked title is this one.
    /// </summary>
    /// <param name="blocked">The blocked title.</param>
    /// <param name="type">The type.</param>
    /// <param name="ids">The provider ids.</param>
    /// <returns>True when it is.</returns>
    public static bool Matches(BlockedTitle blocked, RequestType type, IReadOnlyDictionary<string, string> ids)
    {
        ArgumentNullException.ThrowIfNull(blocked);
        return blocked.Type == type && ProviderIds.Overlap(IdsOf(blocked), ids);
    }

    /// <summary>
    /// Returns a blocked title's provider ids.
    /// </summary>
    /// <param name="blocked">The blocked title.</param>
    /// <returns>Its ids.</returns>
    public static Dictionary<string, string> IdsOf(BlockedTitle blocked)
    {
        ArgumentNullException.ThrowIfNull(blocked);
        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in new[] { (ProviderIds.Tmdb, blocked.Tmdb), (ProviderIds.Tvdb, blocked.Tvdb), (ProviderIds.Imdb, blocked.Imdb) })
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                ids[key] = value;
            }
        }

        return ProviderIds.Clean(ids);
    }
}
