using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Requests;

/// <summary>
/// The title filter reads the page's query, places each title where its badge says, and asks TMDb for what it narrows by.
/// </summary>
public class TitleFilterTests
{
    private static SearchResult Title(RequestType type = RequestType.Movie, params string[] genres) => new()
    {
        Type = type, Title = "A Title", Genres = new List<string>(genres), ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "1" }
    };

    /// <summary>
    /// The query is read as Jellyfin writes it, and what the filter does not know is left out.
    /// True: the page's filter narrows by the states, types, and genres chosen.
    /// False: an unknown value would match nothing, or the empty filter would hide titles.
    /// </summary>
    [Fact]
    public void Parse_JellyfinQuery_KeepsWhatItKnows()
    {
        var filter = TitleFilter.Parse(false, "requested, monitored ,Nonsense", "Series,Book", "Drama|Comedy|");

        Assert.False(filter.InLibrary);
        Assert.Equal(new[] { "Requested", "Monitored" }, filter.StatesWanted);
        Assert.Equal(new[] { RequestType.Series }, filter.Types);
        Assert.Equal(new[] { "Drama", "Comedy" }, filter.Genres);
        Assert.True(TitleFilter.Parse(null, null, null, null).IsEmpty);
    }

    /// <summary>
    /// A title stands where its badge says, with Radarr's files counting as downloaded only for those who see them.
    /// True: the filter and the cards agree on each title's state.
    /// False: a title would be filtered under a state its card does not show.
    /// </summary>
    [Fact]
    public void StateOf_EachBadge_IsItsState()
    {
        Assert.Equal(TitleFilter.NotRequested, TitleFilter.StateOf(Title()));
        Assert.Equal(TitleFilter.Monitored, TitleFilter.StateOf(new SearchResult { InArr = true }));
        Assert.Equal(TitleFilter.Downloaded, TitleFilter.StateOf(new SearchResult { Type = RequestType.Movie, InArr = true, ArrHasFiles = true, Downloaded = true }));

        // Radarr's files alone say Downloaded only to administrators.
        Assert.Equal(TitleFilter.Monitored, TitleFilter.StateOf(new SearchResult { Type = RequestType.Movie, InArr = true, ArrHasFiles = true }));
        Assert.Equal(TitleFilter.Downloaded, TitleFilter.StateOf(new SearchResult { Type = RequestType.Movie, InArr = true, ArrHasFiles = true }, seesArrFiles: true));
        Assert.Equal(TitleFilter.Monitored, TitleFilter.StateOf(new SearchResult { Type = RequestType.Series, InArr = true, ArrHasFiles = true }));

        // Administrators' cards call a show Sonarr has some of Partly Downloaded.
        Assert.Equal(TitleFilter.Downloaded, TitleFilter.StateOf(new SearchResult { Type = RequestType.Series, InArr = true, ArrHasFiles = true }, seesArrFiles: true));
        Assert.Equal(TitleFilter.Requested, TitleFilter.StateOf(new SearchResult { RequestStatus = RequestStatus.Pending }));

        // Waiting for approval, as its card says, even when Radarr already has the file.
        Assert.Equal(TitleFilter.Requested, TitleFilter.StateOf(new SearchResult { Type = RequestType.Movie, RequestStatus = RequestStatus.Pending, InArr = true, ArrHasFiles = true }, seesArrFiles: true));
        Assert.Equal(TitleFilter.Approved, TitleFilter.StateOf(new SearchResult { RequestStatus = RequestStatus.Failed }));
        Assert.Equal(TitleFilter.Rejected, TitleFilter.StateOf(new SearchResult { RequestStatus = RequestStatus.Declined, InArr = true }));
    }

    /// <summary>
    /// Any choice within a section matches, and every section must.
    /// True: a filter of comedy movies shows comedy movies only, and a part show counts as not in the library.
    /// False: a filter would show too much or too little.
    /// </summary>
    [Fact]
    public void Matches_Sections_AnyChoiceAndEverySection()
    {
        var owned = Title(RequestType.Movie, "Drama");
        owned.LibraryItemId = Guid.NewGuid();
        var partShow = Title(RequestType.Series, "Comedy");
        partShow.LibraryItemId = Guid.NewGuid();
        partShow.Partial = true;
        var wanted = Title(RequestType.Movie, "Comedy");

        var notInLibrary = TitleFilter.Parse(false, null, null, null);
        var comedyMovies = TitleFilter.Parse(null, "NotRequested,Requested", "Movie", "comedy|Action");

        Assert.False(notInLibrary.Matches(owned));
        Assert.True(notInLibrary.Matches(partShow));
        Assert.True(notInLibrary.Matches(wanted));
        Assert.True(comedyMovies.Matches(wanted));
        Assert.False(comedyMovies.Matches(partShow));
        Assert.False(comedyMovies.Matches(owned));
        Assert.True(TitleFilter.Parse(null, null, null, null).Matches(owned));
    }

    /// <summary>
    /// TMDb is asked for the order and the bounds chosen, by release date for movies and first air date for shows.
    /// True: a browse list comes back narrowed as the filter shows.
    /// False: TMDb would return a list that does not match the filter.
    /// </summary>
    [Fact]
    public void DiscoverQuery_Choices_AreTmdbsParameters()
    {
        var movies = new TitleFilter().WithDiscover("TopRated", "1990,2000", 7, "Short,Medium", "en,ja", "8,337").DiscoverQuery(RequestType.Movie, "GB");
        var shows = new TitleFilter().WithDiscover("Newest", null, null, "Long", null, null).DiscoverQuery(RequestType.Series, "US");

        Assert.Equal("vote_average.desc", movies["sort_by"]);
        Assert.Equal("1990-01-01", movies["primary_release_date.gte"]);
        Assert.Equal("2009-12-31", movies["primary_release_date.lte"]);
        Assert.Equal("7", movies["vote_average.gte"]);
        Assert.Equal("200", movies["vote_count.gte"]);
        Assert.Equal("1", movies["with_runtime.gte"]);
        Assert.Equal("120", movies["with_runtime.lte"]);
        Assert.Equal("en|ja", movies["with_original_language"]);
        Assert.Equal("8|337", movies["with_watch_providers"]);
        Assert.Equal("GB", movies["watch_region"]);

        // Long runtimes have no upper bound.
        Assert.Equal("first_air_date.desc", shows["sort_by"]);
        Assert.True(shows.ContainsKey("first_air_date.lte"));
        Assert.Equal("121", shows["with_runtime.gte"]);
        Assert.False(shows.ContainsKey("with_runtime.lte"));
    }

    /// <summary>
    /// What is not known is left out, and an order alone narrows nothing but still asks TMDb.
    /// True: a bad value from the address cannot break a list.
    /// False: an unknown sort or decade would reach TMDb, or an order alone would hide titles.
    /// </summary>
    [Fact]
    public void WithDiscover_UnknownValues_AreLeftOut()
    {
        var filter = new TitleFilter().WithDiscover("Sideways", "1995,abc,1900", 12, "Huge", "english,EN,fr", "x,-1,9");
        var ordered = new TitleFilter().WithDiscover("Oldest", null, null, null, null, null);

        Assert.Null(filter.Sort);
        Assert.Equal(new[] { 1900 }, filter.Decades);
        Assert.Null(filter.MinRating);
        Assert.Empty(filter.RuntimesWanted);
        Assert.Equal(new[] { "fr" }, filter.Languages);
        Assert.Equal(new[] { 9 }, filter.Providers);
        Assert.True(ordered.IsEmpty);
        Assert.True(ordered.AsksTmdb);
    }

    /// <summary>
    /// Decades, rating, and language narrow any list, not only TMDb's.
    /// True: a home row or a search shows only titles inside the chosen bounds.
    /// False: titles outside the bounds would show in lists TMDb did not filter.
    /// </summary>
    [Fact]
    public void MatchesTitle_Bounds_NarrowAnyList()
    {
        var filter = new TitleFilter().WithDiscover(null, "1960,1900,1990", 7, null, "en", null);

        Assert.True(filter.MatchesTitle(new SearchResult { Year = 1994, TmdbRating = 8.1, Language = "en" }));
        Assert.True(filter.MatchesTitle(new SearchResult { Year = 1962, TmdbRating = 7.0, Language = "en" }));
        Assert.False(filter.MatchesTitle(new SearchResult { Year = 2004, TmdbRating = 8.1, Language = "en" }));
        Assert.False(filter.MatchesTitle(new SearchResult { Year = 1994, TmdbRating = 6.9, Language = "en" }));
        Assert.False(filter.MatchesTitle(new SearchResult { Year = 1994, TmdbRating = 8.1, Language = "ja" }));
    }
}
