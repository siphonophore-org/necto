using System;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.WireGuard;
using Jellyfin.Plugin.Necto.Services.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.WireGuard;

/// <summary>
/// Adds, saves, and removes peers and connections. It makes the keys a record needs, encrypts every private and
/// preshared key, gives a peer the next free address, and refuses a change that could not run, before anything is
/// stored. The tunnels apply each change at once.
/// </summary>
[PluginService(ServiceLifetime.Singleton)]
public sealed class WireGuardEditor
{
    private readonly IConfigurationStore _store;
    private readonly SecretProtector _secrets;
    private readonly SidecarStore _sidecar;
    private readonly TunnelSupervisor? _supervisor;

    /// <summary>
    /// Initializes a new instance of the <see cref="WireGuardEditor"/> class.
    /// </summary>
    /// <param name="store">Reads and changes the settings.</param>
    /// <param name="secrets">Encrypts the keys.</param>
    /// <param name="sidecar">Derives public keys.</param>
    /// <param name="supervisor">Applies each change to the tunnels, or null in tests.</param>
    public WireGuardEditor(IConfigurationStore store, SecretProtector secrets, SidecarStore sidecar, TunnelSupervisor? supervisor = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        _sidecar = sidecar ?? throw new ArgumentNullException(nameof(sidecar));
        _supervisor = supervisor;
    }

    /// <summary>
    /// Adds a peer, or saves the one with the given id. A new peer with a public key brought its own key pair, and one
    /// without gets a new pair. A saved peer keeps its keys.
    /// </summary>
    /// <param name="id">The peer to save, or null to add one.</param>
    /// <param name="input">The peer's fields.</param>
    /// <returns>The stored peer.</returns>
    /// <exception cref="ArgumentException">When the peer could not run.</exception>
    /// <exception cref="InvalidOperationException">When the peer needs a new key pair and the sidecar is not downloaded.</exception>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">When no peer has the id.</exception>
    public TunnelPeer SavePeer(Guid? id, PeerInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            throw new ArgumentException("Enter a name.");
        }

        var ownKey = (input.PublicKey ?? string.Empty).Trim();
        string? newPrivate = null;
        string? newPublic = null;
        if (id is null && !TunnelKeys.IsValid(ownKey))
        {
            (newPrivate, newPublic) = NewKeyPair();
        }

        TunnelPeer? saved = null;
        Change(settings =>
        {
            var peer = id is { } existing
                ? settings.Peers.FirstOrDefault(p => p.Id == existing) ?? throw new System.Collections.Generic.KeyNotFoundException()
                : null;
            if (peer is null)
            {
                peer = new TunnelPeer { Id = Guid.NewGuid(), CreatedUtc = DateTime.UtcNow };
                if (newPrivate is null)
                {
                    peer.PublicKey = ownKey;
                }
                else
                {
                    peer.PrivateKey = _secrets.Protect(newPrivate);
                    peer.PublicKey = newPublic!;
                }

                settings.Peers.Add(peer);
            }

            peer.Name = input.Name.Trim();
            peer.UserId = input.UserId;
            peer.Address = (input.Address ?? string.Empty).Trim();
            peer.RoutedSubnets = (input.RoutedSubnets ?? new()).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            peer.Enabled = input.Enabled;
            peer.DownloadMbps = input.DownloadMbps;
            peer.UploadMbps = input.UploadMbps;
            peer.DataLimit = input.DataLimit;
            peer.DataPeriod = input.DataPeriod;
            if (settings.UsePresharedKeys && !SecretProtector.IsProtected(peer.PresharedKey) && !TunnelKeys.IsValid(peer.PresharedKey))
            {
                peer.PresharedKey = _secrets.Protect(TunnelKeys.NewPresharedKey());
            }

            if (string.IsNullOrWhiteSpace(peer.Address))
            {
                peer.Address = SidecarConfigBuilder.NextFreeAddress(settings.TunnelSubnet, settings.Peers.Where(p => p != peer).Select(p => p.Address))
                    ?? throw new ArgumentException("The tunnel subnet has no free address for another peer.");
            }

            saved = peer;
        });
        return saved!;
    }

    /// <summary>
    /// Removes a peer. Its configuration stops working.
    /// </summary>
    /// <param name="id">The peer.</param>
    /// <returns><c>true</c> when it was removed, <c>false</c> when no peer has the id.</returns>
    public bool RemovePeer(Guid id)
    {
        var removed = false;
        Change(settings => removed = settings.Peers.RemoveAll(p => p.Id == id) > 0);
        return removed;
    }

    /// <summary>
    /// Adds a connection, or saves the one with the given id. A blank private key keeps the stored one, or makes a new
    /// one for a new connection, and a blank preshared key keeps the stored one.
    /// </summary>
    /// <param name="id">The connection to save, or null to add one.</param>
    /// <param name="input">The connection's fields.</param>
    /// <returns>The stored connection.</returns>
    /// <exception cref="ArgumentException">When the connection could not run.</exception>
    /// <exception cref="InvalidOperationException">When a key must be derived and the sidecar is not downloaded.</exception>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">When no connection has the id.</exception>
    public TunnelConnection SaveConnection(Guid? id, ConnectionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Endpoint)
            || !TunnelKeys.IsValid(input.PeerPublicKey) || string.IsNullOrWhiteSpace(input.Address))
        {
            throw new ArgumentException("Enter a name, the remote host, its public key, and this server's address.");
        }

        var pasted = (input.NewPrivateKey ?? string.Empty).Trim();
        if (pasted.Length > 0 && !TunnelKeys.IsValid(pasted))
        {
            throw new ArgumentException("The private key is not a WireGuard key.");
        }

        var psk = (input.NewPresharedKey ?? string.Empty).Trim();
        if (psk.Length > 0 && !TunnelKeys.IsValid(psk))
        {
            throw new ArgumentException("The preshared key is not a WireGuard key.");
        }

        string? privateKey = null;
        string? publicKey = null;
        if (pasted.Length > 0)
        {
            privateKey = pasted;
            publicKey = TunnelKeys.PublicKey(RequireSidecar(), pasted);
        }
        else if (id is null)
        {
            (privateKey, publicKey) = NewKeyPair();
        }

        TunnelConnection? saved = null;
        Change(settings =>
        {
            var connection = id is { } existing
                ? settings.Connections.FirstOrDefault(c => c.Id == existing) ?? throw new System.Collections.Generic.KeyNotFoundException()
                : null;
            if (connection is null)
            {
                connection = new TunnelConnection { Id = Guid.NewGuid() };
                settings.Connections.Add(connection);
            }

            if (privateKey is not null)
            {
                connection.PrivateKey = _secrets.Protect(privateKey);
                connection.PublicKey = publicKey!;
            }

            if (psk.Length > 0)
            {
                connection.PresharedKey = _secrets.Protect(psk);
            }

            connection.Name = input.Name.Trim();
            connection.Endpoint = input.Endpoint.Trim();
            connection.PeerPublicKey = input.PeerPublicKey.Trim();
            connection.Address = input.Address.Trim();
            connection.Forwards = input.Forwards ?? new();
            connection.Enabled = input.Enabled;
            connection.Keepalive = Math.Max(0, input.Keepalive);
            connection.DownloadMbps = input.DownloadMbps;
            connection.UploadMbps = input.UploadMbps;
            connection.DataLimit = input.DataLimit;
            connection.DataPeriod = input.DataPeriod;
            saved = connection;
        });
        return saved!;
    }

    /// <summary>
    /// Removes a connection. This server stops connecting to it.
    /// </summary>
    /// <param name="id">The connection.</param>
    /// <returns><c>true</c> when it was removed, <c>false</c> when no connection has the id.</returns>
    public bool RemoveConnection(Guid id)
    {
        var removed = false;
        Change(settings => removed = settings.Connections.RemoveAll(c => c.Id == id) > 0);
        return removed;
    }

    // Changes a copy of the settings, checks it, and stores it only when it passes, then applies it to the tunnels.
    private void Change(Action<WireGuardSettings> change)
    {
        _store.Mutate(config =>
        {
            var copy = ConfigurationCopy.Of(config.WireGuard);
            change(copy);
            WireGuardValidator.Validate(copy);
            WireGuardValidator.ValidateAddresses(copy);
            config.WireGuard = copy;
            return true;
        });
        _supervisor?.Apply();
    }

    private (string PrivateKey, string PublicKey) NewKeyPair()
    {
        var sidecar = RequireSidecar();
        var privateKey = TunnelKeys.NewPrivateKey();
        return (privateKey, TunnelKeys.PublicKey(sidecar, privateKey));
    }

    private string RequireSidecar()
        => _sidecar.Find() ?? throw new InvalidOperationException("Download wireguard-go in Settings first, so Necto can make the keys.");
}
