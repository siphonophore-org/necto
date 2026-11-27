using Jellyfin.Plugin.Necto.Services.Requests;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Requests;

/// <summary>
/// The details service shows people's photos only from the artwork hosts, at the size the page shows.
/// </summary>
public class DetailsServiceTests
{
    /// <summary>
    /// A person's photo comes only from TMDb's image host over https, resized.
    /// True: the page loads photos from the host its policy allows.
    /// False: a tracker's or a plain http image would be shown.
    /// </summary>
    [Fact]
    public void PersonImage_Hosts_OnlyTheArtworkHosts()
    {
        Assert.Equal("https://image.tmdb.org/t/p/w185/a.jpg", DetailsService.PersonImage("https://image.tmdb.org/t/p/original/a.jpg"));
        Assert.Null(DetailsService.PersonImage("https://tracker.example.com/a.jpg"));
        Assert.Null(DetailsService.PersonImage("http://image.tmdb.org/t/p/w185/a.jpg"));
    }
}
