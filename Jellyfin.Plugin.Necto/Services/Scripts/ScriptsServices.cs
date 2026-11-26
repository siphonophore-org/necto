using System;
using Jellyfin.Data.Events.System;
using Jellyfin.Data.Events.Users;
using Jellyfin.Plugin.Necto.Services.Notifications;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Events.Authentication;
using MediaBrowser.Controller.Events.Updates;
using Microsoft.Extensions.DependencyInjection;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Scripts;

/// <summary>
/// Registers the Scripts tool's services, which need more than one registration each: one runner that is both a
/// hosted service and the instance the routes call, and one consumer for every server event it answers.
/// </summary>
public static class ScriptsServices
{
    /// <summary>
    /// Adds the Scripts services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Writes summed script failures and the activity log step to Jellyfin's activity log, and sends each
        // failure to Notifications.
        services.AddSingleton(provider => new ScriptsActivity(
            provider.GetRequiredService<ActivityLogger>(),
            provider.GetRequiredService<IServerConfigurationManager>(),
            provider.GetRequiredService<INotifier>()));

        services.AddSingleton<SecretVault>();
        services.AddSingleton<ScriptPaths>();
        services.AddSingleton<HistoryStore>();
        services.AddSingleton<EventContextBuilder>();
        services.AddSingleton<OperationExecutor>();
        services.AddSingleton<ForEachExpander>();

        // One runner owns the queue, so it is both the hosted service and the instance the dispatcher and the
        // test route call.
        services.AddSingleton<ScriptRunner>();
        services.AddHostedService(provider => provider.GetRequiredService<ScriptRunner>());
        services.AddSingleton<ScriptDispatcher>();
        services.AddHostedService<ScriptGroups>();

        services.AddHostedService<EventSubscriber>();
        services.AddHostedService<ScriptScheduledTasks>();

        // One instance answers every event the server publishes through its event bus.
        services.AddSingleton<ServerEventConsumers>();
        AddConsumer<AuthenticationResultEventArgs>(services);
        AddConsumer<AuthenticationRequestEventArgs>(services);
        AddConsumer<UserCreatedEventArgs>(services);
        AddConsumer<UserDeletedEventArgs>(services);
        AddConsumer<UserUpdatedEventArgs>(services);
        AddConsumer<UserLockedOutEventArgs>(services);
        AddConsumer<UserPasswordChangedEventArgs>(services);
        AddConsumer<PluginInstalledEventArgs>(services);
        AddConsumer<PluginUpdatedEventArgs>(services);
        AddConsumer<PluginUninstalledEventArgs>(services);
        AddConsumer<PluginInstallingEventArgs>(services);
        AddConsumer<PluginInstallationCancelledEventArgs>(services);
        AddConsumer<PendingRestartEventArgs>(services);
    }

    private static void AddConsumer<T>(IServiceCollection services)
        where T : EventArgs
        => services.AddSingleton<IEventConsumer<T>>(provider => (IEventConsumer<T>)provider.GetRequiredService<ServerEventConsumers>());
}
