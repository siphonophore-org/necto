using System.Linq;
using Jellyfin.Plugin.Necto.Services.Storage;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.Configuration;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Storage;

/// <summary>
/// Jellyfin's own folders are named by strings the disk sidecar can show, as the dashboard names them.
/// </summary>
public class JellyfinFoldersTests
{
    /// <summary>
    /// Every folder's key is a built-in string, so the sidecar's folder table shows words such as Program data.
    /// True: each folder reads as the dashboard names it.
    /// False: the table would show a raw key such as PathProgramData.
    /// </summary>
    [Fact]
    public void Get_EveryKey_IsAString()
    {
        var folders = new JellyfinFolders(Config());
        var english = Siphonophore.Stolon.StolonTranslations.For("en-us");

        var keys = folders.Get().Select(f => f.Key).ToList();

        Assert.Equal(7, keys.Count);
        Assert.All(keys, key => Assert.True(english.ContainsKey(key), key + " has no string"));
        Assert.Equal("Program data", english["LabelProgramData"]);
        Assert.Equal("Image cache", english["LabelImageCache"]);
    }

    /// <summary>
    /// Only the web folder is one Jellyfin reads without writing.
    /// True: a read-only web folder never shows as a problem.
    /// False: a server whose web files are read only would show a folder it cannot write to.
    /// </summary>
    [Fact]
    public void ReadOnly_IsTheWebFolderOnly()
    {
        var keys = new JellyfinFolders(Config()).Get().Select(f => f.Key).ToList();

        Assert.Equal(new[] { "LabelWeb" }, keys.Where(JellyfinFolders.ReadOnly));
    }

    // A server configuration whose paths are folders of their own, with the default transcode folder.
    private static IServerConfigurationManager Config()
    {
        var paths = InterfaceStub.Of<IServerApplicationPaths>();
        return InterfaceStub.Of<IServerConfigurationManager>(
            ("get_ApplicationPaths", _ => paths),
            ("get_CommonApplicationPaths", _ => paths),
            ("get_Configuration", _ => new ServerConfiguration()),
            ("GetConfiguration", _ => new EncodingOptions()));
    }
}
