using System;
using Jellyfin.Plugin.Necto.Configuration.WireGuard;
using Jellyfin.Plugin.Necto.Services.WireGuard;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.WireGuard;

/// <summary>
/// A configuration save checks the WireGuard settings and keeps the peers, connections, and host keys stored.
/// </summary>
public class WireGuardValidatorTests
{
    /// <summary>
    /// A save from Settings keeps the stored peers, connections, and host keys, whatever it carries.
    /// True: a page read before a peer was added never removes it.
    /// False: saving Settings drops a peer or replaces the host's key.
    /// </summary>
    [Fact]
    public void Saving_KeepsTheStoredRecordsAndKeys()
    {
        var stored = new WireGuardSettings { HostPrivateKey = "enc:v1:key", HostPublicKey = "public", Peers = { new TunnelPeer { Name = "Phone" } }, Connections = { new TunnelConnection { Name = "Friend" } } };
        var incoming = new WireGuardSettings { ListenPort = 51900 };

        var saved = WireGuardValidator.Saving(incoming, stored);

        Assert.Equal(51900, saved.ListenPort);
        Assert.Same(stored.Peers, saved.Peers);
        Assert.Same(stored.Connections, saved.Connections);
        Assert.Equal(("enc:v1:key", "public"), (saved.HostPrivateKey, saved.HostPublicKey));
    }

    /// <summary>
    /// A save with a setting that could not run is refused, and one with no WireGuard part takes the defaults.
    /// True: a bad port never reaches the tunnels, and a page that knows nothing of WireGuard saves.
    /// False: the host fails to start, or another tool's save fails.
    /// </summary>
    [Fact]
    public void Saving_RefusesBadSettingsAndFillsMissingOnes()
    {
        Assert.Throws<ArgumentException>(() => WireGuardValidator.Saving(new WireGuardSettings { ListenPort = 0 }, new WireGuardSettings()));

        Assert.Equal(51820, WireGuardValidator.Saving(null, null).ListenPort);
    }
}
