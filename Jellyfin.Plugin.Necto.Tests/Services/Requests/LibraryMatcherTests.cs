using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Requests;

/// <summary>
/// The library matcher finds a requested title in the library by its ids or its name and year, counts a show's
/// episodes by season, and reads the viewer's own rows. It sets Jellyfin's static library, so it runs on its own.
/// </summary>
[Collection(ServicesCollection.Name)]
public class LibraryMatcherTests
{
    private readonly List<BaseItem> _items = new();
    private readonly List<InternalItemsQuery> _queries = new();
    private readonly ILibraryManager _libraryManager;
    private readonly User _user = new("viewer", "Jellyfin.Server.Implementations.Users.DefaultAuthenticationProvider", "Jellyfin.Server.Implementations.Users.DefaultPasswordResetProvider");

    private List<BaseItem> _newestMovies = new();
    private List<BaseItem> _newestEpisodes = new();
    private List<BaseItem> _played = new();
    private List<BaseItem> _favourites = new();

    public LibraryMatcherTests()
    {
        _libraryManager = InterfaceStub.Of<ILibraryManager>(
            ("GetItemById", a => a[0] is Guid id ? _items.FirstOrDefault(i => i.Id == id) : null),
            ("GetItemList", a => Answer((InternalItemsQuery)a[0]!)));
        BaseItem.LibraryManager = _libraryManager;
    }

    /// <summary>
    /// Titles compare alike whatever their punctuation, accents, case, or spacing.
    /// True: a request for Amélie finds Amelie in the library.
    /// False: a title in the library would be requested again under a slightly different name.
    /// </summary>
    [Theory]
    [InlineData("Spider-Man: Into the Spider-Verse", "spider man into the spider verse")]
    [InlineData("Amélie", "amelie")]
    [InlineData("  WALL·E ", "wall-e")]
    public void NormalizeTitle_Spellings_CompareAlike(string a, string b)
        => Assert.Equal(LibraryMatcher.NormalizeTitle(a), LibraryMatcher.NormalizeTitle(b));

    /// <summary>
    /// A library item matches only the same title and year, under its name or its original title.
    /// True: Dune (2021) matches DUNE (2021), and a foreign film matches its own title.
    /// False: a remake or a sequel would be taken for the title asked for.
    /// </summary>
    [Fact]
    public void SameTitle_TitleAndYear_MatchOnlyTogether()
    {
        var movie = new Movie { Name = "Dune", OriginalTitle = "Dune", ProductionYear = 2021 };
        var foreign = new Movie { Name = "Spirited Away", OriginalTitle = "千と千尋の神隠し", ProductionYear = 2001 };

        Assert.True(LibraryMatcher.SameTitle(movie, "DUNE", 2021));
        Assert.False(LibraryMatcher.SameTitle(movie, "Dune", 1984));
        Assert.False(LibraryMatcher.SameTitle(movie, "Dune: Part Two", 2021));
        Assert.True(LibraryMatcher.SameTitle(foreign, "千と千尋の神隠し", 2001));
    }

    /// <summary>
    /// A file name's year and id tags are stripped from a title.
    /// True: an item still named after its folder compares by its title alone.
    /// False: an item named The Matrix (1999) [tmdbid-603] would never match The Matrix.
    /// </summary>
    [Theory]
    [InlineData("The Matrix (1999) [tmdbid-603]", "The Matrix")]
    [InlineData("Dune Part Two (2024) {imdb-tt15239678}", "Dune Part Two")]
    [InlineData("Blade Runner 2049", "Blade Runner 2049")]
    [InlineData("1917 (2019)", "1917")]
    public void StripFileTags_FileName_IsItsTitle(string name, string expected)
        => Assert.Equal(expected, LibraryMatcher.StripFileTags(name));

    /// <summary>
    /// An item still named after its folder matches its title and year.
    /// True: a title the library has not named yet still fulfils its request.
    /// False: the request would wait for a title already in the library.
    /// </summary>
    [Fact]
    public void SameTitle_ItemNamedAfterItsFolder_Matches()
        => Assert.True(LibraryMatcher.SameTitle(new Movie { Name = "The Matrix (1999) [tmdbid-603]", ProductionYear = 1999 }, "The Matrix", 1999));

    /// <summary>
    /// A title match needs the name and year, and no id that conflicts.
    /// True: The Thing (1982) with an IMDb id matches the library's, and one with another TMDb id does not.
    /// False: a different film of the same name and year would fulfil the request.
    /// </summary>
    [Fact]
    public void IsTitleMatch_ConflictingIds_NeverMatch()
    {
        var item = new Movie { Name = "The Thing", ProductionYear = 1982 };
        item.ProviderIds[ProviderIds.Tmdb] = "1091";

        Assert.True(LibraryMatcher.IsTitleMatch(item, new Dictionary<string, string> { [ProviderIds.Imdb] = "tt0084787" }, "The Thing", 1982));
        Assert.False(LibraryMatcher.IsTitleMatch(item, new Dictionary<string, string> { [ProviderIds.Tmdb] = "99999" }, "The Thing", 1982));
    }

    /// <summary>
    /// A file holding two episodes counts for both, and missing episodes and specials are counted apart.
    /// True: Sonarr's two episodes in one file complete a two episode season.
    /// False: a season would never count as complete, or a missing episode would count as there.
    /// </summary>
    [Fact]
    public void Count_DoubleEpisodeFile_CountsForBoth()
    {
        var counts = LibraryMatcher.Count(new BaseItem[]
        {
            Episode(1, 1, 2),
            Episode(1, 3),
            Episode(1, 3),
            Episode(1, 4, null, isVirtual: true),
            Episode(2, null),
            Episode(0, 1)
        });
        var twoInOne = LibraryMatcher.Count(new BaseItem[] { Episode(1, 1, 2) });

        Assert.Equal(3, counts.Have[1]);
        Assert.Equal(1, counts.Have[2]);
        Assert.Equal(1, counts.Have[0]);
        Assert.Contains(1, counts.Missing);
        Assert.Equal(new List<int> { 1 }, LibraryMatcher.CompleteSeasons(twoInOne, new Dictionary<int, int> { [1] = 2 }));
    }

    /// <summary>
    /// Specials never make a season complete.
    /// True: a show's requested seasons are judged by their own episodes.
    /// False: a show with only specials would read as having a complete season.
    /// </summary>
    [Fact]
    public void CompleteSeasons_SpecialsOnly_IsNone()
    {
        var counts = new SeasonCounts();
        counts.Have[0] = 4;

        Assert.Empty(LibraryMatcher.CompleteSeasons(counts, null));
    }

    /// <summary>
    /// Recently added puts movies and shows in one newest first order, a show by its newest episode.
    /// True: the row shows what arrived most recently, whatever its type.
    /// False: the row would list a type first, or a show by its oldest episode.
    /// </summary>
    [Fact]
    public void RecentlyAdded_MoviesAndShows_NewestFirst()
    {
        var newest = AddMovie("Newest", 1);
        var oldest = AddMovie("Oldest", 9);
        var andor = AddShow("Andor");
        var bear = AddShow("The Bear");
        _newestMovies = new List<BaseItem> { newest, oldest };
        _newestEpisodes = new List<BaseItem> { Episode(andor, 3), Episode(andor, 4), Episode(bear, 5) };

        var found = Matcher().RecentlyAdded(new[] { RequestType.Movie, RequestType.Series }, 10, _user);

        Assert.Equal(new BaseItem[] { newest, andor, bear, oldest }, found);
        Assert.All(_queries, q => Assert.Equal((ItemSortBy.DateCreated, SortOrder.Descending), Assert.Single(q.OrderBy)));
    }

    /// <summary>
    /// Recently added reads only the types asked for and stops at the limit.
    /// True: a viewer who may request only shows sees only shows.
    /// False: the row would hold titles the viewer cannot request, or more than it shows.
    /// </summary>
    [Fact]
    public void RecentlyAdded_TypesAndLimit_AreHeldTo()
    {
        var andor = AddShow("Andor");
        _newestMovies = new List<BaseItem> { AddMovie("Newest", 1) };
        _newestEpisodes = new List<BaseItem> { Episode(andor, 3) };

        var shows = Matcher().RecentlyAdded(new[] { RequestType.Series }, 10, _user);
        var one = Matcher().RecentlyAdded(new[] { RequestType.Movie, RequestType.Series }, 1, _user);

        Assert.Equal(new BaseItem[] { andor }, shows);
        Assert.Equal("Newest", Assert.Single(one).Name);
    }

    /// <summary>
    /// Recently watched takes each title once, a show by its episodes, then fills up with favourites.
    /// True: recommendations draw on what the viewer watched and liked.
    /// False: a show would be listed once per episode, or favourites would be left out.
    /// </summary>
    [Fact]
    public void RecentlyWatched_Played_EachTitleOnceThenFavourites()
    {
        var andor = AddShow("Andor");
        var bear = AddShow("The Bear");
        var dune = AddMovie("Dune");
        var heat = AddMovie("Heat");
        _played = new List<BaseItem> { Episode(andor, 0), Episode(andor, 0), dune, Episode(bear, 0) };
        _favourites = new List<BaseItem> { dune, heat };

        var watched = Matcher().RecentlyWatched(new[] { RequestType.Movie, RequestType.Series }, 4, _user);

        Assert.Equal(new BaseItem[] { andor, dune, bear, heat }, watched);
        var played = _queries.Single(q => q.IsPlayed == true);
        Assert.Equal((ItemSortBy.DatePlayed, SortOrder.Descending), Assert.Single(played.OrderBy));
        Assert.Equal(new[] { BaseItemKind.Movie, BaseItemKind.Episode }, played.IncludeItemTypes);
    }

    /// <summary>
    /// Recently watched stops at the limit without reading favourites.
    /// True: a viewer with enough played titles costs one read.
    /// False: favourites would be read for nothing on every visit.
    /// </summary>
    [Fact]
    public void RecentlyWatched_LimitReached_ReadsNoFavourites()
    {
        _played = new List<BaseItem> { AddMovie("Dune"), AddMovie("Heat") };

        var watched = Matcher().RecentlyWatched(new[] { RequestType.Movie }, 1, _user);

        Assert.Equal("Dune", Assert.Single(watched).Name);
        Assert.DoesNotContain(_queries, q => q.IsFavorite == true);
    }

    // The library as Jellyfin's database would answer each read the rows make, already in the order asked for.
    private List<BaseItem> Answer(InternalItemsQuery query)
    {
        _queries.Add(query);
        var kinds = query.IncludeItemTypes ?? Array.Empty<BaseItemKind>();
        if (query.IsPlayed == true)
        {
            return _played;
        }

        if (query.IsFavorite == true)
        {
            return _favourites;
        }

        if (query.OrderBy.Count == 0)
        {
            return new List<BaseItem>();
        }

        return kinds.Contains(BaseItemKind.Episode) ? _newestEpisodes : kinds.Contains(BaseItemKind.Movie) ? _newestMovies : new List<BaseItem>();
    }

    private LibraryMatcher Matcher() => new(_libraryManager, InterfaceStub.Of<IUserDataManager>(), InterfaceStub.Of<IUserManager>());

    private Movie AddMovie(string name, int daysAgo = 0)
    {
        var movie = new Movie { Id = Guid.NewGuid(), Name = name, DateCreated = DateTime.UtcNow.AddDays(-daysAgo) };
        _items.Add(movie);
        return movie;
    }

    private Series AddShow(string name)
    {
        var series = new Series { Id = Guid.NewGuid(), Name = name };
        _items.Add(series);
        return series;
    }

    private Episode Episode(Series series, int daysAgo)
    {
        var episode = new Episode { Id = Guid.NewGuid(), Name = series.Name + " episode", SeriesId = series.Id, DateCreated = DateTime.UtcNow.AddDays(-daysAgo) };
        _items.Add(episode);
        return episode;
    }

    private static Episode Episode(int season, int? first, int? last = null, bool isVirtual = false)
        => new() { ParentIndexNumber = season, IndexNumber = first, IndexNumberEnd = last, IsVirtualItem = isVirtual };
}
