using System;
using Jellyfin.Plugin.Necto.Services.Requests;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Requests;

/// <summary>
/// The web asset cache keeps what it found in the web client for good, and looks again a few minutes after finding nothing.
/// </summary>
public class WebAssetCacheTests
{
    /// <summary>
    /// A found asset is looked up once.
    /// True: the web client's folder is read once per start.
    /// False: every page load would read the web client's files again.
    /// </summary>
    [Fact]
    public void Get_Found_IsLookedUpOnce()
    {
        var lookups = 0;
        var now = DateTime.UtcNow;
        var cache = new WebAssetCache<string>(css => css is { Length: > 0 }, () => now);

        Assert.Equal("a{}", cache.Get(() => { lookups++; return "a{}"; }));
        now = now.AddDays(1);
        Assert.Equal("a{}", cache.Get(() => { lookups++; return "b{}"; }));
        Assert.Equal(1, lookups);
    }

    /// <summary>
    /// Nothing found is kept for a few minutes, then looked for again, and what is found then is kept.
    /// True: a web client installed after the plugin is picked up soon, without a read per page load.
    /// False: the page would never find the web client's files, or read the folder on every hit.
    /// </summary>
    [Fact]
    public void Get_NothingFound_LooksAgainAfterTheWindow()
    {
        var lookups = 0;
        var now = DateTime.UtcNow;
        var found = string.Empty;
        var cache = new WebAssetCache<string>(css => css is { Length: > 0 }, () => now);
        string? Find()
        {
            lookups++;
            return found;
        }

        Assert.Equal(string.Empty, cache.Get(Find));
        found = "a{}";
        now = now.AddMinutes(1);
        Assert.Equal(string.Empty, cache.Get(Find));
        Assert.Equal(1, lookups);

        now = now.Add(WebAssetCache<string>.RetryAfter);
        Assert.Equal("a{}", cache.Get(Find));
        Assert.Equal(2, lookups);
        now = now.AddHours(1);
        Assert.Equal("a{}", cache.Get(Find));
        Assert.Equal(2, lookups);
    }
}
