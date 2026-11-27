using System;
using System.Linq;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Ddns;

/// <summary>
/// Registers the DDNS tool's services: every DNS provider, the provider HTTP client, the public IP lookup, the status
/// store, and the update run. Credentials are kept with Necto's secret protector.
/// </summary>
public static class DdnsServices
{
    /// <summary>
    /// The stable name Necto's key ring folder, Data Protection application name and purpose come from. Changing it
    /// makes every stored secret unreadable.
    /// </summary>
    public const string SecretsName = "Jellyfin.Plugin.Necto";

    /// <summary>
    /// Adds the DDNS services to the container.
    /// </summary>
    /// <param name="services">The container.</param>
    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var providers = typeof(DdnsServices).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IDNSProvider).IsAssignableFrom(t));
        foreach (var provider in providers)
        {
            services.AddSingleton(typeof(IDNSProvider), provider);
        }

        services.AddPluginSecretProtector(SecretsName);

        // Provider requests carry API keys in headers. Their client follows a redirect only on the same server.
        services.AddHttpClient(ProviderHttp.ClientName)
            .ConfigurePrimaryHttpMessageHandler(ProviderHttp.PrimaryHandler)
            .AddHttpMessageHandler(() => new SameHostRedirectHandler());
        services.AddSingleton<IPDetectionService>();
        services.AddSingleton<DNSLookupService>();
        services.AddSingleton<StatusStoreService>();
        services.AddSingleton<DNSUpdateService>();
        services.AddSingleton<IHostedService, ServerTextSetup>();
    }
}
