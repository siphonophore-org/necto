using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.Necto.Services.WireGuard;

/// <summary>
/// Starts the WireGuard tunnels with the server and stops them with it.
/// </summary>
[PluginService(ServiceLifetime.Singleton, ServiceType = typeof(IHostedService))]
public sealed class TunnelHost : IHostedService
{
    private readonly TunnelSupervisor _supervisor;

    /// <summary>
    /// Initializes a new instance of the <see cref="TunnelHost"/> class.
    /// </summary>
    /// <param name="supervisor">Runs the tunnels.</param>
    public TunnelHost(TunnelSupervisor supervisor)
    {
        _supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _supervisor.Start();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => _supervisor.StopAsync(cancellationToken);
}
