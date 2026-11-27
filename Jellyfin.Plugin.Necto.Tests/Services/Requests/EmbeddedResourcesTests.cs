using System;
using Jellyfin.Plugin.Necto.Services.Requests;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Requests;

/// <summary>
/// The request page's files are read from the plugin's own assembly, with a short notice for one that is not there.
/// </summary>
public class EmbeddedResourcesTests
{
    /// <summary>
    /// The request page, its web client script, and its manifest are built into the plugin.
    /// True: the page is served from the plugin with nothing on disk.
    /// False: the page would serve its unavailable notice.
    /// </summary>
    [Theory]
    [InlineData("necto_requests_page.html", "<html")]
    [InlineData("necto_requests_webclient.js", "function")]
    [InlineData("necto_requests_manifest.webmanifest", "\"start_url\"")]
    public void Read_PagesFiles_AreBuiltIn(string file, string holds)
        => Assert.Contains(holds, EmbeddedResources.Read(file), StringComparison.Ordinal);

    /// <summary>
    /// A file that is not there reads as a short notice.
    /// True: a missing file shows a notice rather than failing the request.
    /// False: a missing file would throw.
    /// </summary>
    [Fact]
    public void Read_MissingFile_IsANotice()
        => Assert.Equal("<p>The request page is unavailable.</p>", EmbeddedResources.Read("missing.html"));

    /// <summary>
    /// The app icons open from the plugin, and an unknown one is none.
    /// True: the installed app's icons are served.
    /// False: an icon would be missing, or an unknown name would open something else.
    /// </summary>
    [Fact]
    public void OpenIcon_KnownAndUnknown_OpensOnlyTheIcon()
    {
        using var icon = EmbeddedResources.OpenIcon("requests-192.png");

        Assert.NotNull(icon);
        Assert.True(icon!.Length > 0);
        Assert.Null(EmbeddedResources.OpenIcon("other.png"));
    }
}
