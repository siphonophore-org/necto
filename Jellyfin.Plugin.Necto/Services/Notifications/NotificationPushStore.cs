using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.Necto.Services.Configuration;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Notifications;

/// <summary>
/// Gives Stolon the VAPID keys browser notifications are signed with, making a pair the first time one is needed, and
/// keeps the browsers each user turned notifications on in, in a file beside the configuration.
/// </summary>
[PluginService(ServiceLifetime.Singleton, ServiceType = typeof(IWebPushStore))]
public sealed class NotificationPushStore : IWebPushStore
{
    /// <summary>The file the subscriptions are kept in, in the plugin configurations folder.</summary>
    public const string FileName = "Jellyfin.Plugin.Necto.Push.json";

    private readonly IConfigurationStore _store;
    private readonly SecretProtector _secrets;
    private readonly JsonFileStore<List<WebPushSubscription>> _file;
    private readonly object _lock = new();
    private (string Public, string Private, string Contact, string Server, WebPushKeys? Keys)? _keys;

    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationPushStore"/> class.
    /// </summary>
    /// <param name="store">Reads and saves the keys in the configuration.</param>
    /// <param name="secrets">Encrypts the private key.</param>
    /// <param name="paths">Gives the folder the subscriptions are kept in.</param>
    /// <param name="logger">Writes the server log.</param>
    public NotificationPushStore(IConfigurationStore store, SecretProtector secrets, IApplicationPaths paths, ILogger<NotificationPushStore> logger)
        : this(store, secrets, Path.Join((paths ?? throw new ArgumentNullException(nameof(paths))).PluginConfigurationsPath, FileName), logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationPushStore"/> class with its file at a path.
    /// </summary>
    /// <param name="store">Reads and saves the keys in the configuration.</param>
    /// <param name="secrets">Encrypts the private key.</param>
    /// <param name="path">The file the subscriptions are kept in.</param>
    /// <param name="logger">Writes the server log.</param>
    public NotificationPushStore(IConfigurationStore store, SecretProtector secrets, string path, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(secrets);
        _store = store;
        _secrets = secrets;
        _file = new JsonFileStore<List<WebPushSubscription>>(path, logger);
    }

    /// <inheritdoc />
    public WebPushKeys? GetKeys()
    {
        var (publicKey, privateKey, contact, server) = _store.Read(c => (c.Notifications?.PushPublicKey ?? string.Empty, c.Notifications?.PushPrivateKey ?? string.Empty, c.Notifications?.PushContact ?? string.Empty, c.Notifications?.ServerUrl ?? string.Empty));

        // Reading the keys checks that they pair, so they are read once until a setting they depend on changes.
        lock (_lock)
        {
            if (_keys is { } kept && kept.Public == publicKey && kept.Private == privateKey && kept.Contact == contact && kept.Server == server)
            {
                return kept.Keys;
            }

            var keys = WebPushKeys.FromStored(publicKey, privateKey, _secrets, contact, server);
            if (keys is null && _secrets.IsAvailable)
            {
                keys = MakeKeys();
                return keys;
            }

            _keys = (publicKey, privateKey, contact, server, keys);
            return keys;
        }
    }

    /// <summary>
    /// Makes a new VAPID key pair and forgets every subscription, since a browser signed up with the old key cannot get
    /// a notification signed with the new one.
    /// </summary>
    /// <returns>How many subscriptions were forgotten.</returns>
    public int ResetKeys()
    {
        lock (_lock)
        {
            MakeKeys();
        }

        var removed = 0;
        _file.UpdateIf(list =>
        {
            removed = list.Count;
            list.Clear();
            return removed > 0;
        });
        return removed;
    }

    /// <summary>
    /// Lists every subscription.
    /// </summary>
    /// <returns>The subscriptions, oldest first.</returns>
    public IReadOnlyList<WebPushSubscription> All() => _file.Load().OrderBy(s => s.CreatedUtc).ToList();

    /// <inheritdoc />
    public IReadOnlyList<WebPushSubscription> GetSubscriptions(Guid userId) => _file.Load().Where(s => s.UserId == userId).ToList();

    /// <inheritdoc />
    public void Save(WebPushSubscription subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        _file.Update(list =>
        {
            list.RemoveAll(s => string.Equals(s.Endpoint, subscription.Endpoint, StringComparison.Ordinal));
            list.Add(subscription);
        });
    }

    /// <inheritdoc />
    public void Remove(string endpoint)
        => _file.UpdateIf(list => list.RemoveAll(s => string.Equals(s.Endpoint, endpoint, StringComparison.Ordinal)) > 0);

    /// <summary>
    /// Forgets every subscription of a user, such as one who was deleted.
    /// </summary>
    /// <param name="userId">The user.</param>
    public void RemoveUser(Guid userId) => _file.UpdateIf(list => list.RemoveAll(s => s.UserId == userId) > 0);

    // Called under _lock.
    private WebPushKeys MakeKeys()
    {
        var made = WebPushKeys.Generate();
        var protectedKey = made.ProtectPrivateKey(_secrets);
        _store.Mutate(c =>
        {
            c.Notifications ??= new Jellyfin.Plugin.Necto.Configuration.Notifications.NotificationSettings();
            c.Notifications.PushPublicKey = made.PublicKey;
            c.Notifications.PushPrivateKey = protectedKey;
            return true;
        });
        var (contact, server) = _store.Read(c => (c.Notifications.PushContact ?? string.Empty, c.Notifications.ServerUrl ?? string.Empty));
        var keys = WebPushKeys.FromStored(made.PublicKey, protectedKey, _secrets, contact, server) ?? made;
        _keys = (made.PublicKey, protectedKey, contact, server, keys);
        return keys;
    }
}
