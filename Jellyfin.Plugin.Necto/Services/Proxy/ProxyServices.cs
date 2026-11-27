using System;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Services.Proxy.Reachability;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Events.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Proxy;

/// <summary>
/// Registers the Proxy tool's services: the proxy and its certificates, trusting the proxy in Jellyfin, the outside
/// checks, and the visitor history with its bans, block list and locations.
/// </summary>
public static class ProxyServices
{
    /// <summary>
    /// Adds the Proxy services to the container. The proxy stays stopped until it is turned on.
    /// </summary>
    /// <param name="services">The container.</param>
    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Sums bursts, such as a flood of bans, into one activity entry per outcome in the server's display language.
        // The container disposes it on shutdown, which writes whatever is still waiting.
        services.TryAddSingleton(sp => new ActivitySummary(sp.GetRequiredService<ActivityLogger>(), language: () => ServerText.Language));
        services.AddSingleton(sp => new ProxyPaths(sp.GetRequiredService<IApplicationPaths>()));
        services.AddSingleton<CertificateSet>();
        services.AddSingleton<Http01ChallengeStore>();
        services.AddSingleton<AcmeCertificateService>();
        services.AddSingleton<ReachabilityChecker>();
        services.AddSingleton<PublicAddresses>();
        services.AddSingleton<ProxyManager>();
        services.AddSingleton<JellyfinNetworkOrchestrator>();
        services.AddHostedService<FolderProtection>();
        services.AddHostedService<ProxyHostedService>();

        // Visitors: who connected from where. Sign-ins come from Jellyfin's events, sessions from a poll, and the
        // address each person used from the proxy.
        services.AddSingleton<AccessRecorder>();
        services.AddSingleton<GeoIpService>();
        services.AddSingleton<ServerLocation>();
        services.AddSingleton<BanList>();
        services.AddSingleton<BlockFilter>();
        services.AddSingleton<VisitorStore>();
        services.AddSingleton<VisitorService>();
        services.AddScoped<IEventConsumer<AuthenticationRequestEventArgs>, SignInFailedConsumer>();
        services.AddScoped<IEventConsumer<AuthenticationResultEventArgs>, SignInSucceededConsumer>();
        services.AddHostedService<VisitorHostedService>();
    }
}
