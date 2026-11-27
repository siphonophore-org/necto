using System.Text.Json;
using Jellyfin.Plugin.Necto.Configuration;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Configuration;

/// <summary>
/// Only the options of a copy kept on the server carry the mark, however alike other options are.
/// </summary>
public class ServerCopyMarkerTests
{
    /// <summary>
    /// The server copy's options are marked, and fresh or default options are not.
    /// True: only a server copy writes a secret's value.
    /// False: options that look alike could share metadata and write secrets to a page, or a copy could lose them.
    /// </summary>
    [Fact]
    public void Marks_OnlyTheServerCopyOptions()
    {
        Assert.True(ServerCopyMarker.Marks(ConfigurationCopy.Json));
        Assert.False(ServerCopyMarker.Marks(new JsonSerializerOptions()));
        Assert.False(ServerCopyMarker.Marks(JsonSerializerOptions.Default));
    }
}
