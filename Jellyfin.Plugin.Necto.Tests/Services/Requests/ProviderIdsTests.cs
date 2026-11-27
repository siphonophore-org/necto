using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Services.Requests;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Requests;

/// <summary>
/// Provider ids are kept under the known keys in one spelling, so two ids for the same title always match.
/// </summary>
public class ProviderIdsTests
{
    /// <summary>
    /// Clean keeps only the TMDb, IMDb, and TVDb ids, trimmed, and drops empty ones.
    /// True: a request holds the ids Radarr, Sonarr, and the library are matched by.
    /// False: an unknown or blank id would be stored and matched against.
    /// </summary>
    [Fact]
    public void Clean_MixedKeys_KeepsTheMatchedKeys()
    {
        var ids = ProviderIds.Clean(new Dictionary<string, string> { ["tmdb"] = " 603 ", ["Imdb"] = "tt0133093", ["Trakt"] = "1", ["Tvdb"] = "" });

        Assert.Equal(2, ids.Count);
        Assert.Equal("603", ids["Tmdb"]);
        Assert.True(ProviderIds.Overlap(ids, new Dictionary<string, string> { ["Imdb"] = "TT0133093" }));
        Assert.False(ProviderIds.Overlap(ids, new Dictionary<string, string> { ["Tvdb"] = "603" }));
    }

    /// <summary>
    /// Leading zeros and case are taken out, and an id of only zeros is dropped.
    /// True: 0603 and 603 are the same movie to the blocklist and the open requests, as to TMDb and Radarr.
    /// False: the same title asked for with another spelling of its id would be requested twice.
    /// </summary>
    [Fact]
    public void Clean_OtherSpellings_AreKeptInOne()
    {
        var ids = ProviderIds.Clean(new Dictionary<string, string> { ["tmdb"] = " 000603 ", ["IMDB"] = "TT0133093", ["Tvdb"] = "000" });

        Assert.Equal("603", ids[ProviderIds.Tmdb]);
        Assert.Equal("tt0133093", ids[ProviderIds.Imdb]);
        Assert.False(ids.ContainsKey(ProviderIds.Tvdb));
        Assert.True(ProviderIds.Overlap(ids, ProviderIds.Clean(new Dictionary<string, string> { ["Tmdb"] = "603" })));
    }

    /// <summary>
    /// Two sets of ids conflict only when they hold different ids under the same key.
    /// True: a title with another TMDb id is never taken for the one asked for.
    /// False: an unrelated title would match, or one with only another provider's id would be refused.
    /// </summary>
    [Fact]
    public void Conflict_SameKeyDifferentId_Conflicts()
    {
        Assert.True(ProviderIds.Conflict(new Dictionary<string, string> { ["Tmdb"] = "1" }, new Dictionary<string, string> { ["Tmdb"] = "2" }));
        Assert.False(ProviderIds.Conflict(new Dictionary<string, string> { ["Tmdb"] = "1" }, new Dictionary<string, string> { ["Imdb"] = "tt1" }));
    }
}
