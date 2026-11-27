using System;
using System.IO;
using Jellyfin.Plugin.Necto.Configuration.Notifications;
using Jellyfin.Plugin.Necto.Services.Configuration;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Notifications;

/// <summary>
/// Registers Stolon's web push and held message stores over Necto's files, with the VAPID public key in the
/// configuration and the private key in the <see cref="NotificationSecretStore"/>.
/// </summary>
public static class NotificationServices
{
    /// <summary>The file the browsers users turned notifications on in are kept in, in the plugin configurations folder.</summary>
    public const string PushFileName = "Jellyfin.Plugin.Necto.Push.json";

    /// <summary>The file held Jellyfin messages are kept in, in the plugin configurations folder.</summary>
    public const string HeldFileName = "Jellyfin.Plugin.Necto.Held.json";

    /// <summary>
    /// Adds the web push and held message stores to the container, each as its own type and as Stolon's interface.
    /// </summary>
    /// <param name="services">The container.</param>
    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(sp => new JsonNotifyHeldStore(
            Path.Join(sp.GetRequiredService<IApplicationPaths>().PluginConfigurationsPath, HeldFileName),
            logger: sp.GetService<ILogger<JsonNotifyHeldStore>>()));
        services.AddSingleton<INotifyHeldStore>(sp => sp.GetRequiredService<JsonNotifyHeldStore>());
        services.AddSingleton(sp => CreatePushStore(
            sp.GetRequiredService<IConfigurationStore>(),
            sp.GetRequiredService<SecretProtector>(),
            sp.GetRequiredService<NotificationSecretStore>(),
            Path.Join(sp.GetRequiredService<IApplicationPaths>().PluginConfigurationsPath, PushFileName),
            sp.GetService<ILogger<JsonWebPushStore>>()));
        services.AddSingleton<IWebPushStore>(sp => sp.GetRequiredService<JsonWebPushStore>());
    }

    /// <summary>
    /// Builds the web push store over Necto's subscription file and its stored keys.
    /// </summary>
    /// <param name="store">Reads and saves the public key, the contact, and the server address in the configuration.</param>
    /// <param name="secrets">Encrypts the private key.</param>
    /// <param name="kept">Keeps the private key.</param>
    /// <param name="path">The file the subscriptions are kept in.</param>
    /// <param name="logger">Writes the server log, or <c>null</c>.</param>
    /// <returns>The store.</returns>
    public static JsonWebPushStore CreatePushStore(IConfigurationStore store, SecretProtector secrets, NotificationSecretStore kept, string path, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(kept);
        return new JsonWebPushStore(
            path,
            secrets,
            () =>
            {
                var (publicKey, contact, server) = store.Read(c => (c.Notifications?.PushPublicKey ?? string.Empty, c.Notifications?.PushContact ?? string.Empty, c.Notifications?.ServerUrl ?? string.Empty));
                return new WebPushStoredKeys(publicKey, kept.Load().PushPrivateKey, contact, server);
            },
            keys =>
            {
                kept.SetPushPrivateKey(keys.ProtectedPrivateKey);
                store.Mutate(c =>
                {
                    c.Notifications ??= new NotificationSettings();
                    c.Notifications.PushPublicKey = keys.PublicKey;
                    return true;
                });
            },
            logger);
    }
}
