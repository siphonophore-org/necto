using Jellyfin.Plugin.Necto.Services.Storage;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Storage;

public class DismissedNoticesTests
{
    [Fact]
    public void For_GivesOnlyThatDisksKeys()
    {
        var entries = new[]
        {
            DismissedNotices.Entry("/media", "readonly:/media/movies"),
            DismissedNotices.Entry("/media2", "readonly:/media2/tv"),
            DismissedNotices.Entry("/media", "server:/media/cache")
        };

        Assert.Equal(new[] { "readonly:/media/movies", "server:/media/cache" }, DismissedNotices.For(entries, "/media"));
        Assert.Empty(DismissedNotices.For(entries, "/other"));
    }
}
