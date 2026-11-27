using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Requests;

/// <summary>
/// The blocklist refuses a title by its listing, genre, rating, words, TMDb keywords, language, or collection, and
/// gives the same answer however often it is asked or the lists change.
/// </summary>
public class BlocklistTests
{
    public static IEnumerable<object?[]> Titles()
    {
        var none = Ids();
        yield return new object?[] { RequestType.Movie, Ids(("Tmdb", "536869")), "Cats", null, null, null };
        yield return new object?[] { RequestType.Movie, Ids(("tmdb", "536869")), "Cats", null, null, null };
        yield return new object?[] { RequestType.Movie, Ids(("Imdb", "TT5697572")), "Cats", null, null, null };
        yield return new object?[] { RequestType.Series, Ids(("Tmdb", "536869")), "Cats the show", null, null, null };
        yield return new object?[] { RequestType.Series, Ids(("Tvdb", "81189")), "Show", null, null, null };
        yield return new object?[] { RequestType.Series, Ids(("Tvdb", " 81189")), "Show", null, null, null };
        yield return new object?[] { RequestType.Movie, Ids(("Tvdb", "81189")), "Show", null, null, null };
        yield return new object?[] { RequestType.Movie, Ids(("Tmdb", "603")), "The Matrix", new[] { "Action", "HORROR" }, "R", null };
        yield return new object?[] { RequestType.Movie, Ids(("Tmdb", "603")), "The Matrix", new[] { "Reality", "Horror" }, null, null };
        yield return new object?[] { RequestType.Movie, Ids(("Tmdb", "603")), "The Matrix", new[] { "Action" }, " nc-17 ", null };
        yield return new object?[] { RequestType.Movie, Ids(("Tmdb", "603")), "The Matrix", null, "  ", null };
        yield return new object?[] { RequestType.Movie, none, "Dune: Part Two", null, null, null };
        yield return new object?[] { RequestType.Movie, none, "Dune Part Twofold", null, null, null };
        yield return new object?[] { RequestType.Movie, none, "xxx files", null, null, null };
        yield return new object?[] { RequestType.Movie, none, "unicode tales", null, null, null };
        yield return new object?[] { RequestType.Movie, none, null, null, null, null };
        yield return new object?[] { RequestType.Movie, none, "Anything", null, null, new TitleTraits(new[] { 9715, 210024 }, null, null) };
        yield return new object?[] { RequestType.Movie, none, "Anything", null, null, new TitleTraits(Array.Empty<int>(), "ja", null) };
        yield return new object?[] { RequestType.Movie, none, "Anything", null, null, new TitleTraits(Array.Empty<int>(), "KO", 726871) };
        yield return new object?[] { RequestType.Movie, none, "Anything", null, null, new TitleTraits(Array.Empty<int>(), "en", 726871) };
        yield return new object?[] { RequestType.Movie, none, "Anything", null, null, new TitleTraits(new[] { 1 }, "en", 1) };
    }

    /// <summary>
    /// A title is blocked by its listing for its type, a genre, a rating, or a whole word of its name.
    /// True: Cats the movie, reality shows, NC-17 titles, and xXx are refused, and Maxxxine is not.
    /// False: a blocked title could be requested, or one only sharing letters with a word would be refused.
    /// </summary>
    [Fact]
    public void Reason_TitleGenreRatingAndWord_Block()
    {
        var config = new RequestSettings
        {
            BlockedTitles = { new BlockedTitle { Type = RequestType.Movie, Title = "Cats", Tmdb = "536869" } },
            BlockedGenres = { "Reality" },
            BlockedRatings = { "NC-17" },
            BlockedKeywords = { "xxx" }
        };

        Assert.NotNull(Blocklist.Reason(config, RequestType.Movie, Ids(("Tmdb", "536869")), "Cats", null, null));
        Assert.Null(Blocklist.Reason(config, RequestType.Series, Ids(("Tmdb", "536869")), "Cats", null, null));
        Assert.NotNull(Blocklist.Reason(config, RequestType.Series, Ids(("Tmdb", "1")), "Love Island", new[] { "reality" }, null));
        Assert.NotNull(Blocklist.Reason(config, RequestType.Movie, Ids(("Tmdb", "2")), "Showgirls", null, "nc-17"));
        Assert.NotNull(Blocklist.Reason(config, RequestType.Movie, Ids(("Tmdb", "3")), "xXx: Return of Xander Cage", null, null));
        Assert.Null(Blocklist.Reason(config, RequestType.Movie, Ids(("Tmdb", "4")), "Maxxxine", null, "R"));
    }

    /// <summary>
    /// A title is blocked by a TMDb keyword, its original language, or its collection, once TMDb's answer is known.
    /// True: each reason names what blocked it, and without TMDb's answer these blocks are left to the request rules.
    /// False: an anime title or a Japanese film would get through, or one would be blocked on no evidence.
    /// </summary>
    [Fact]
    public void Reason_KeywordLanguageAndCollection_Block()
    {
        var config = new RequestSettings
        {
            BlockedTags = { new TmdbName { Id = 210024, Name = "anime" } },
            BlockedLanguages = { "ja" },
            BlockedCollections = { new TmdbName { Id = 726871, Name = "Dune Collection" } }
        };

        Assert.True(Blocklist.NeedsTraits(config));
        Assert.Equal("Titles tagged “anime” are blocked", Blocklist.Reason(config, RequestType.Series, Ids(("Tmdb", "1")), "A Show", null, null, new TitleTraits(new[] { 5, 210024 }, "en", null)));
        Assert.Equal("Titles in Japanese are blocked", Blocklist.Reason(config, RequestType.Movie, Ids(("Tmdb", "2")), "A Film", null, null, new TitleTraits(Array.Empty<int>(), "JA", null)));
        Assert.Equal("Dune Collection is blocked", Blocklist.Reason(config, RequestType.Movie, Ids(("Tmdb", "3")), "Dune", null, null, new TitleTraits(Array.Empty<int>(), "en", 726871)));
        Assert.Null(Blocklist.Reason(config, RequestType.Movie, Ids(("Tmdb", "4")), "Arrival", null, null, new TitleTraits(new[] { 5 }, "en", 1)));
        Assert.Null(Blocklist.Reason(config, RequestType.Movie, Ids(("Tmdb", "3")), "Dune", null, null));
    }

    /// <summary>
    /// The blocklist read ahead gives the answers a plain check of each list gives, the first time and after.
    /// True: reading the lists once per change never changes who is refused.
    /// False: a title would be blocked or let through differently once the lists were read ahead.
    /// </summary>
    [Theory]
    [MemberData(nameof(Titles))]
    public void Reason_ReadAhead_GivesThePlainAnswers(RequestType type, Dictionary<string, string> ids, string? title, string[]? genres, string? rating, TitleTraits? traits)
    {
        var config = Config();

        Assert.Equal(Plain(config, type, ids, title, genres, rating, traits), Blocklist.Reason(config, type, ids, title, genres, rating, traits));
        Assert.Equal(Plain(config, type, ids, title, genres, rating, traits), Blocklist.Reason(config, type, ids, title, genres, rating, traits));
        Assert.Equal(Plain(new RequestSettings(), type, ids, title, genres, rating, traits), Blocklist.Reason(new RequestSettings(), type, ids, title, genres, rating, traits));
    }

    /// <summary>
    /// A blocklist changed in place or replaced is read again.
    /// True: a block or unblock takes effect at once.
    /// False: the old lists would keep deciding until a restart.
    /// </summary>
    [Fact]
    public void Reason_ChangedBlocklist_IsReadAgain()
    {
        var config = new RequestSettings();
        var ids = Ids(("Tmdb", "603"));
        Assert.Null(Blocklist.Reason(config, RequestType.Movie, ids, "The Matrix", null, null));

        config.BlockedKeywords.Add("matrix");
        Assert.Equal("Titles with “matrix” are blocked", Blocklist.Reason(config, RequestType.Movie, ids, "The Matrix", null, null));

        config.BlockedTitles = new List<BlockedTitle> { new() { Type = RequestType.Movie, Title = "The Matrix", Tmdb = "603" } };
        Assert.Equal("Blocked by an administrator", Blocklist.Reason(config, RequestType.Movie, ids, "The Matrix", null, null));

        config.BlockedTitles = new List<BlockedTitle>();
        config.BlockedKeywords = new List<string>();
        Assert.Null(Blocklist.Reason(config, RequestType.Movie, ids, "The Matrix", null, null));
    }

    // The blocklist checked list by list, without reading ahead, to compare with.
    private static string? Plain(RequestSettings config, RequestType type, IReadOnlyDictionary<string, string> ids, string? title, IEnumerable<string>? genres, string? rating, TitleTraits? traits = null)
    {
        if (config.BlockedTitles.Any(t => Blocklist.Matches(t, type, ids)))
        {
            return "Blocked by an administrator";
        }

        if (genres?.FirstOrDefault(g => config.BlockedGenres.Contains(g, StringComparer.OrdinalIgnoreCase)) is { } genre)
        {
            return "The " + genre + " genre is blocked";
        }

        if (!string.IsNullOrWhiteSpace(rating) && config.BlockedRatings.Contains(rating.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            return rating.Trim() + " titles are blocked";
        }

        if (traits is not null)
        {
            if (config.BlockedTags.FirstOrDefault(t => traits.Keywords.Contains(t.Id)) is { } tag)
            {
                return "Titles tagged “" + tag.Name + "” are blocked";
            }

            if (!string.IsNullOrWhiteSpace(traits.Language) && config.BlockedLanguages.FirstOrDefault(l => string.Equals(l, traits.Language, StringComparison.OrdinalIgnoreCase)) is { } language)
            {
                var culture = System.Globalization.CultureInfo.GetCultureInfo(language);
                return "Titles in " + (culture.EnglishName.StartsWith("Unknown", StringComparison.Ordinal) ? language : culture.EnglishName) + " are blocked";
            }

            if (traits.Collection is { } collection && config.BlockedCollections.FirstOrDefault(c => c.Id == collection) is { } set)
            {
                return set.Name + " is blocked";
            }
        }

        var normalized = " " + LibraryMatcher.NormalizeWords(title) + " ";
        return config.BlockedKeywords.FirstOrDefault(k => LibraryMatcher.NormalizeWords(k) is { Length: > 0 } word && normalized.Contains(" " + word + " ", StringComparison.Ordinal)) is { } keyword
            ? "Titles with “" + keyword + "” are blocked"
            : null;
    }

    private static RequestSettings Config() => new()
    {
        BlockedTitles =
        {
            new BlockedTitle { Type = RequestType.Movie, Title = "Cats", Tmdb = "536869", Imdb = "tt5697572" },
            new BlockedTitle { Type = RequestType.Series, Title = "Show", Tvdb = " 81189 " },
            new BlockedTitle { Type = RequestType.Movie, Title = "Bad id", Tmdb = "not a number" }
        },
        BlockedGenres = { "Horror", "reality" },
        BlockedRatings = { "NC-17", "TV-MA" },
        BlockedKeywords = { "XXX", "Part Two", "   ", "Ünïcode" },
        BlockedTags = { new TmdbName { Id = 210024, Name = "anime" }, new TmdbName { Id = 9715, Name = "superhero" } },
        BlockedLanguages = { "JA", "ko" },
        BlockedCollections = { new TmdbName { Id = 726871, Name = "Dune Collection" } }
    };

    private static Dictionary<string, string> Ids(params (string Key, string Value)[] ids)
        => ids.ToDictionary(i => i.Key, i => i.Value, StringComparer.OrdinalIgnoreCase);
}
