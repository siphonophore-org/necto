using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.WireGuard;
using Jellyfin.Plugin.Necto.Services.Configuration;
using Jellyfin.Plugin.Necto.Services.Notifications;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.WireGuard;

/// <summary>
/// Writes tunnel events to Jellyfin's activity log in the server's language, and sends each to Notifications.
/// </summary>
[PluginService(ServiceLifetime.Singleton)]
public sealed class TunnelActivity
{
    private const string Type = "WireGuard";

    private readonly ActivityLogger _activity;
    private readonly INotifier _notifier;
    private readonly IConfigurationStore _store;
    private readonly IUserManager _users;
    private readonly IServerConfigurationManager _config;

    /// <summary>
    /// Initializes a new instance of the <see cref="TunnelActivity"/> class.
    /// </summary>
    /// <param name="activity">Writes the activity log.</param>
    /// <param name="notifier">Sends each event to Notifications.</param>
    /// <param name="store">Reads the peers and connections, to name them.</param>
    /// <param name="users">Names a peer's owner.</param>
    /// <param name="config">Gives the server's display language.</param>
    public TunnelActivity(ActivityLogger activity, INotifier notifier, IConfigurationStore store, IUserManager users, IServerConfigurationManager config)
    {
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
        _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>Logs a peer or connection coming up.</summary>
    /// <param name="tunnelKey">The tunnel the event came from.</param>
    /// <param name="publicKey">The peer's public key.</param>
    /// <param name="endpoint">The address the peer connected from.</param>
    public void PeerUp(string tunnelKey, string publicKey, string? endpoint)
    {
        var language = StolonTranslations.ServerLanguage(_config);
        var from = HostOf(endpoint);
        if (tunnelKey == SidecarConfigBuilder.HostKey)
        {
            var peer = PeerByKey(publicKey);
            var owner = OwnerName(peer);
            Write(
                WireGuardEvents.Connected,
                TranslationKeys.WireGuardActivityNamePeerConnected,
                StolonTranslations.Format(TranslationKeys.WireGuardActivityPeerConnected, language, owner, peer?.Name ?? Short(publicKey), from),
                LogLevel.Information,
                peer?.UserId ?? default,
                language,
                new Dictionary<string, string> { ["peer"] = peer?.Name ?? Short(publicKey), ["owner"] = owner, ["from"] = from });
            return;
        }

        var name = ConnectionName(tunnelKey);
        Write(
            WireGuardEvents.ConnectionEstablished,
            TranslationKeys.WireGuardActivityNameConnectionEstablished,
            StolonTranslations.Format(TranslationKeys.WireGuardActivityConnectionEstablished, language, name, from),
            LogLevel.Information,
            default,
            language,
            new Dictionary<string, string> { ["connection"] = name, ["from"] = from });
    }

    /// <summary>Logs a peer or connection going quiet.</summary>
    /// <param name="tunnelKey">The tunnel the event came from.</param>
    /// <param name="publicKey">The peer's public key.</param>
    public void PeerDown(string tunnelKey, string publicKey)
    {
        var language = StolonTranslations.ServerLanguage(_config);
        if (tunnelKey == SidecarConfigBuilder.HostKey)
        {
            var peer = PeerByKey(publicKey);
            var owner = OwnerName(peer);
            Write(
                WireGuardEvents.Disconnected,
                TranslationKeys.WireGuardActivityNamePeerDisconnected,
                StolonTranslations.Format(TranslationKeys.WireGuardActivityPeerDisconnected, language, owner, peer?.Name ?? Short(publicKey)),
                LogLevel.Information,
                peer?.UserId ?? default,
                language,
                new Dictionary<string, string> { ["peer"] = peer?.Name ?? Short(publicKey), ["owner"] = owner });
            return;
        }

        // The sidecar keeps retrying, so a lost connection is a warning.
        var name = ConnectionName(tunnelKey);
        Write(
            WireGuardEvents.ConnectionLost,
            TranslationKeys.WireGuardActivityNameConnectionLost,
            StolonTranslations.Format(TranslationKeys.WireGuardActivityConnectionLost, language, name),
            LogLevel.Warning,
            default,
            language,
            new Dictionary<string, string> { ["connection"] = name });
    }

    /// <summary>Logs a tunnel that keeps failing to start. It is still tried again each minute.</summary>
    /// <param name="tunnelKey">The tunnel.</param>
    /// <param name="error">The sidecar's last error line.</param>
    public void TunnelFailed(string tunnelKey, string? error)
    {
        var language = StolonTranslations.ServerLanguage(_config);
        var name = tunnelKey == SidecarConfigBuilder.HostKey
            ? StolonTranslations.Get(TranslationKeys.WireGuardTabHost, language)
            : ConnectionName(tunnelKey);
        Write(
            WireGuardEvents.TunnelFailed,
            TranslationKeys.WireGuardActivityNameTunnelFailed,
            StolonTranslations.Format(TranslationKeys.WireGuardActivityTunnelFailed, language, name, ActivityText.Sentence(error) ?? "?"),
            LogLevel.Error,
            default,
            language,
            new Dictionary<string, string> { ["tunnel"] = name, ["error"] = error ?? string.Empty });
    }

    /// <summary>Logs a peer or connection that reached its data limit and was cut off until the limit's window ends.</summary>
    /// <param name="id">The peer or connection.</param>
    public void DataLimitReached(Guid id)
    {
        var language = StolonTranslations.ServerLanguage(_config);
        var (peer, connection) = _store.Read(c => (c.WireGuard.Peers.FirstOrDefault(p => p.Id == id), c.WireGuard.Connections.FirstOrDefault(x => x.Id == id)));
        string overview;
        string name;
        if (peer is not null)
        {
            name = peer.Name;
            overview = StolonTranslations.Format(TranslationKeys.WireGuardActivityPeerDataLimit, language, OwnerName(peer), peer.Name);
        }
        else
        {
            name = connection?.Name ?? id.ToString("N");
            overview = StolonTranslations.Format(TranslationKeys.WireGuardActivityConnectionDataLimit, language, name);
        }

        // It stays cut off until the limit's window ends or an administrator raises the limit.
        Write(
            WireGuardEvents.DataLimitReached,
            TranslationKeys.WireGuardActivityNameDataLimitReached,
            overview,
            LogLevel.Warning,
            peer?.UserId ?? default,
            language,
            new Dictionary<string, string> { ["name"] = name });
    }

    /// <summary>
    /// Takes the address without the port, which changes with every reconnect and says nothing to an administrator.
    /// </summary>
    /// <param name="endpoint">The address and port, such as 203.0.113.5:51820 or [2001:db8::1]:51820.</param>
    /// <returns>The address, or ? when there is none.</returns>
    public static string HostOf(string? endpoint)
    {
        if (string.IsNullOrEmpty(endpoint))
        {
            return "?";
        }

        var colon = endpoint.LastIndexOf(':');
        var bracket = endpoint.LastIndexOf(']');
        if (endpoint.StartsWith('[') && bracket > 0)
        {
            return endpoint[1..bracket];
        }

        // A bare IPv6 address has several colons and no port.
        if (endpoint.Count(c => c == ':') > 1)
        {
            return endpoint;
        }

        return (colon > 0 ? endpoint[..colon] : endpoint).Trim('[', ']');
    }

    private void Write(string key, string nameKey, string overview, LogLevel level, Guid userId, string language, IReadOnlyDictionary<string, string> data)
    {
        var title = StolonTranslations.Get(nameKey, language);
        _activity.Log(title, Type, overview, level, userId);
        _ = _notifier.SendAsync(new NectoEvent(key, title, overview)
        {
            UserId = userId == Guid.Empty ? null : userId,
            Level = level switch
            {
                LogLevel.Error => NectoEventLevel.Error,
                LogLevel.Warning => NectoEventLevel.Warning,
                _ => NectoEventLevel.Info
            },
            Data = data
        });
    }

    private TunnelPeer? PeerByKey(string publicKey)
        => _store.Read(c => c.WireGuard.Peers.FirstOrDefault(p => p.PublicKey == publicKey));

    private string OwnerName(TunnelPeer? peer)
        => peer is null || peer.UserId == Guid.Empty ? "?" : _users.GetUserById(peer.UserId)?.Username ?? "?";

    private string ConnectionName(string tunnelKey)
        => _store.Read(c => c.WireGuard.Connections.FirstOrDefault(x => x.Id.ToString("N") == tunnelKey)?.Name) ?? tunnelKey;

    private static string Short(string publicKey) => publicKey.Length > 8 ? publicKey[..8] : publicKey;
}
