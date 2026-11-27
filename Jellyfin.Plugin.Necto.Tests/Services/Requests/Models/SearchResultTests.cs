using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Requests.Models;

/// <summary>
/// A search result fills in what it lacks from another result for the same title, and keeps what it has.
/// </summary>
public class SearchResultTests
{
    private static Dictionary<string, string> Ids(string key, string value) => new(StringComparer.OrdinalIgnoreCase) { [key] = value };

    /// <summary>
    /// A library result takes TMDb's rating, genres, and ids but keeps its own poster.
    /// True: a title in the library shows its own artwork with TMDb's details beside it.
    /// False: the library's poster would be replaced, or the details would stay empty.
    /// </summary>
    [Fact]
    public void FillFrom_LibraryResult_FillsOnlyWhatIsMissing()
    {
        var library = new SearchResult { Title = "Dune", PosterUrl = "library.jpg", CommunityRating = 7.9 };

        library.FillFrom(new SearchResult { Title = "Dune", PosterUrl = "remote.jpg", TmdbRating = 8.0, Genres = new List<string> { "Sci-Fi" }, ProviderIds = Ids("Imdb", "tt1160419") });

        Assert.Equal("library.jpg", library.PosterUrl);
        Assert.Equal(8.0, library.TmdbRating);
        Assert.Equal(new[] { "Sci-Fi" }, library.Genres);
        Assert.Equal("tt1160419", library.ProviderIds["Imdb"]);
    }
}
