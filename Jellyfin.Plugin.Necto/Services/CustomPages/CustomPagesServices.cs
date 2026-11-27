using System;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.CustomPages;

/// <summary>
/// Registers the services Pages needs beyond the ones marked for discovery.
/// </summary>
public static class CustomPagesServices
{
    /// <summary>
    /// Registers the stores, their activity, the route credential protector, and the client server side routes use.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<StoreActivity>();

        // One instance holds each store's records behind its own gate, so two would drop each other's writes.
        services.AddSingleton<StoreService>();
        services.AddSingleton<IStoreService>(provider => provider.GetRequiredService<StoreService>());

        // The route passwords share the key ring the settings save uses.
        services.AddPluginSecretProtector(CustomPagesSaving.SecretsName);

        // A route never follows a redirect, so its credentials stay with the host it names and the page sees the
        // redirect itself.
        services
            .AddHttpClient(RouteProxy.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            .SetHandlerLifetime(TimeSpan.FromMinutes(5));
    }
}
