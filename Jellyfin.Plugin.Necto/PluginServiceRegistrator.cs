using System;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto;

/// <summary>
/// Registers the services Necto adds to Jellyfin, which is Stolon's activity logger.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);

        serviceCollection.AddActivityLogger();
    }
}
