using System;
using System.Collections.Generic;
using System.Reflection;
using Jellyfin.Plugin.Necto.Services;
using Jellyfin.Plugin.Necto.Services.Scripts;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto;

/// <summary>
/// Registers every class marked with <see cref="PluginServiceAttribute"/>, and Stolon's activity logger, secret protector, and notifier.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);

        foreach (var (impl, attr) in DiscoverAnnotatedServices(typeof(PluginServiceRegistrator).Assembly))
        {
            serviceCollection.Add(new ServiceDescriptor(attr.ServiceType ?? impl, impl, attr.Lifetime));
        }

        serviceCollection.AddActivityLogger();

        // Encrypts the WireGuard keys and the script secrets at rest. The name is fixed, since a new one makes every stored secret unreadable.
        serviceCollection.AddPluginSecretProtector("Jellyfin.Plugin.Necto");
        ScriptsServices.Register(serviceCollection);
        Services.Storage.StorageServices.Register(serviceCollection);
        Services.CustomPages.CustomPagesServices.Register(serviceCollection);
        serviceCollection.AddBaseNotifier();
        Services.Ddns.DdnsServices.Register(serviceCollection);
        Services.Proxy.ProxyServices.Register(serviceCollection);
    }

    /// <summary>
    /// Finds every concrete class in the assembly that carries the service attribute.
    /// </summary>
    /// <param name="assembly">The assembly to search.</param>
    /// <returns>Each class with its attribute.</returns>
    public static IEnumerable<(Type Impl, PluginServiceAttribute Attr)> DiscoverAnnotatedServices(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
            {
                continue;
            }

            if (type.GetCustomAttribute<PluginServiceAttribute>(inherit: false) is { } attr)
            {
                yield return (type, attr);
            }
        }
    }
}
