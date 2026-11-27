using Jellyfin.Plugin.Necto.Services.Storage;
using Xunit;

using Jellyfin.Plugin.Necto.Tests.Fakes.Storage;

namespace Jellyfin.Plugin.Necto.Tests.Services.Storage;

/// <summary>
/// Covers the rules for a folder made on a disk: one plain name, directly on the disk.
/// </summary>
public class FolderTests
{
    [Theory]
    [InlineData("Movies", true)]
    [InlineData("4K Movies", true)]
    [InlineData("Shows (Kids)", true)]
    [InlineData("", false)]
    [InlineData(".", false)]
    [InlineData("..", false)]
    [InlineData("../etc", false)]
    [InlineData("Movies/4K", false)]
    [InlineData("Movies\\4K", false)]
    [InlineData(" Movies", false)]
    [InlineData("Movies.", false)]
    [InlineData("What?", false)]
    public void AFolderNameIsOnePlainName(string name, bool valid) => Assert.Equal(valid, FolderNames.IsValid(name));

    [Fact]
    public void AFolderSitsDirectlyOnItsDisk()
    {
        Assert.Equal("/mnt/cache/Movies", FolderNames.PathOn("/mnt/cache", "Movies"));
        Assert.Equal("/mnt/cache/Movies", FolderNames.PathOn("/mnt/cache/", "Movies"));
        Assert.Null(FolderNames.PathOn("/mnt/cache", "../etc"));
    }
}
