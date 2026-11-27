using System;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.Necto.Services.Requests;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Requests;

/// <summary>
/// The library refresher scans only a title's own folder inside a library, and asks for a full scan at most hourly.
/// </summary>
public class LibraryRefresherTests
{
    /// <summary>
    /// Only folders inside a library, never its root, a neighbour, or a path that climbs out, are refreshed on their own.
    /// True: an arrival scans its folder alone, on any system's paths.
    /// False: a crafted path would scan outside the libraries, or a library's root would rescan it whole.
    /// </summary>
    [Fact]
    public void Within_Paths_OnlyFoldersInsideALibrary()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        var sep = Path.DirectorySeparatorChar;
        string P(params string[] parts) => Path.Combine(new[] { root }.Concat(parts).ToArray());
        var roots = new[] { P("media", "movies") + sep, P("media", "shows") + sep };

        Assert.True(LibraryRefresher.Within(P("media", "movies", "Inception (2010)"), roots));
        Assert.True(LibraryRefresher.Within(P("media", "shows", "Severance") + sep, roots));
        Assert.True(LibraryRefresher.Within(P("media", "movies", "Vol..2 (2001)"), roots));
        Assert.False(LibraryRefresher.Within(P("media", "movies"), roots));
        Assert.False(LibraryRefresher.Within(P("media", "movies-4k", "Inception (2010)"), roots));
        Assert.False(LibraryRefresher.Within(P("data", "movies", "Inception (2010)"), roots));
        Assert.False(LibraryRefresher.Within(P("media", "movies", "..", "..", "etc"), roots));
        Assert.False(LibraryRefresher.Within(Path.Combine("movies", "Inception (2010)"), roots));
        Assert.False(LibraryRefresher.Within(null, roots));
    }

    /// <summary>
    /// Titles without a folder are left to the library's scans, and a missing folder never counts as outside every library.
    /// True: a title Radarr gave no path scans nothing extra.
    /// False: a blank path would be taken as outside the libraries and scan them all.
    /// </summary>
    [Fact]
    public void Sort_TitlesWithoutAFolder_AreLeftOut()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        var library = Path.Combine(root, "media", "movies") + Path.DirectorySeparatorChar;

        var said = LibraryRefresher.Said(new[] { null, " ", Path.Combine(library, "Dune (2021)") });
        var (inside, outside) = LibraryRefresher.Sort(said, new[] { library });
        var (_, elsewhere) = LibraryRefresher.Sort(new[] { Path.Combine(root, "downloads", "Dune (2021)") }, new[] { library });

        Assert.Single(said);
        Assert.Single(inside);
        Assert.False(outside);
        Assert.Empty(LibraryRefresher.Said(new string?[] { null }));
        Assert.True(elsewhere);
    }

    /// <summary>
    /// A full scan for a folder outside the libraries is asked for at most hourly.
    /// True: titles saved outside every library cost one scan an hour.
    /// False: each such arrival would scan every library.
    /// </summary>
    [Fact]
    public void FullScanDue_WithinTheHour_IsNotDue()
    {
        var now = DateTime.UtcNow;

        Assert.True(LibraryRefresher.FullScanDue(null, now));
        Assert.False(LibraryRefresher.FullScanDue(now.AddMinutes(-59), now));
        Assert.True(LibraryRefresher.FullScanDue(now - LibraryRefresher.FullScanEvery, now));
    }
}
