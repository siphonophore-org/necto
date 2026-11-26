using System;
using System.Collections.Generic;
using System.IO;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.WireGuard;
using Jellyfin.Plugin.Necto.Services.WireGuard;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Siphonophore.Stolon;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.WireGuard;

/// <summary>
/// The editor stores a peer or connection only when the result could run, gives a peer an address and a preshared key,
/// and never makes keys without the sidecar.
/// </summary>
public sealed class WireGuardEditorTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "necto-editor-" + Guid.NewGuid().ToString("N"));
    private readonly SidecarStore _sidecar;
    private readonly FakeConfigurationStore _store;
    private readonly WireGuardEditor _editor;

    public WireGuardEditorTests()
    {
        _sidecar = new SidecarStore(_folder, null, NullLogger<SidecarStore>.Instance);
        _store = new FakeConfigurationStore(new PluginConfiguration
        {
            WireGuard = new WireGuardSettings { Enabled = true, HostEnabled = true, TunnelSubnet = "10.66.0.0/24" }
        });
        _editor = new WireGuardEditor(_store, new SecretProtector("test", NullLogger.Instance), _sidecar);
    }

    /// <summary>
    /// A new peer that brings its own public key is stored with the next free address and a preshared key, and no private key.
    /// True: a device with its own key pair joins without the sidecar making one.
    /// False: the peer has no address, or a private key it never had.
    /// </summary>
    [Fact]
    public void SavePeer_OwnKey_TakesTheNextAddressAndAPresharedKey()
    {
        var key = TunnelKeys.NewPrivateKey();

        var peer = _editor.SavePeer(null, new PeerInput { Name = " Phone ", PublicKey = key, UserId = Guid.NewGuid() });

        Assert.Equal("Phone", peer.Name);
        Assert.Equal("10.66.0.2", peer.Address);
        Assert.Equal(key, peer.PublicKey);
        Assert.Empty(peer.PrivateKey);
        Assert.True(TunnelKeys.IsValid(peer.PresharedKey));
        Assert.Single(_store.Configuration.WireGuard.Peers);
    }

    /// <summary>
    /// A new peer that needs a key pair is refused while wireguard-go is not downloaded, and nothing is stored.
    /// True: the page says to download wireguard-go first.
    /// False: a peer is stored with no public key and cannot connect.
    /// </summary>
    [Fact]
    public void SavePeer_NeedsKeysWithoutTheSidecar_IsRefused()
    {
        Assert.Throws<InvalidOperationException>(() => _editor.SavePeer(null, new PeerInput { Name = "Phone" }));

        Assert.Empty(_store.Configuration.WireGuard.Peers);
    }

    /// <summary>
    /// A peer that could not run, such as one outside the tunnel subnet, is refused and the stored peers stay as they were.
    /// True: a bad address never reaches the tunnel.
    /// False: the sidecar fails to start on a bad peer.
    /// </summary>
    [Fact]
    public void SavePeer_BadAddress_IsRefusedAndNothingChanges()
    {
        Assert.Throws<ArgumentException>(() => _editor.SavePeer(null, new PeerInput { Name = "Phone", PublicKey = TunnelKeys.NewPrivateKey(), Address = "192.168.1.5" }));

        Assert.Empty(_store.Configuration.WireGuard.Peers);
        Assert.Equal(0, _store.Saves);
    }

    /// <summary>
    /// A saved peer keeps its keys, and a peer can be removed.
    /// True: editing a peer never breaks its config file.
    /// False: an edit makes new keys and every device must import again.
    /// </summary>
    [Fact]
    public void SavePeer_Existing_KeepsItsKeysAndRemoves()
    {
        var key = TunnelKeys.NewPrivateKey();
        var peer = _editor.SavePeer(null, new PeerInput { Name = "Phone", PublicKey = key });
        var psk = peer.PresharedKey;

        var saved = _editor.SavePeer(peer.Id, new PeerInput { Name = "Tablet", PublicKey = TunnelKeys.NewPrivateKey(), Enabled = false });

        Assert.Equal(key, saved.PublicKey);
        Assert.Equal(psk, saved.PresharedKey);
        Assert.False(saved.Enabled);
        Assert.True(_editor.RemovePeer(peer.Id));
        Assert.False(_editor.RemovePeer(peer.Id));
        Assert.Throws<KeyNotFoundException>(() => _editor.SavePeer(Guid.NewGuid(), new PeerInput { Name = "x" }));
    }

    /// <summary>
    /// A connection needs its name, remote host, remote key, and address, and a new one needs the sidecar for its keys.
    /// True: the page says what is missing, or to download wireguard-go.
    /// False: a connection that cannot start is stored.
    /// </summary>
    [Fact]
    public void SaveConnection_RefusesMissingFieldsAndMissingSidecar()
    {
        Assert.Throws<ArgumentException>(() => _editor.SaveConnection(null, new ConnectionInput { Name = "Friend" }));
        Assert.Throws<InvalidOperationException>(() => _editor.SaveConnection(null, new ConnectionInput
        {
            Name = "Friend",
            Endpoint = "vpn.example.com:51820",
            PeerPublicKey = TunnelKeys.NewPrivateKey(),
            Address = "10.0.0.5"
        }));

        Assert.Empty(_store.Configuration.WireGuard.Connections);
    }

    public void Dispose()
    {
        _sidecar.Dispose();
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, true);
        }
    }
}
