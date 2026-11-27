using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Configuration;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.Necto.Services.Ddns;

/// <summary>
/// Points <see cref="ServerText"/> at the server's display language as Jellyfin starts, so activity entries and task
/// names follow it.
/// </summary>
public sealed class ServerTextSetup : IHostedService
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ServerTextSetup"/> class.
    /// </summary>
    /// <param name="config">Jellyfin's server configuration, which holds the display language.</param>
    public ServerTextSetup(IServerConfigurationManager config)
    {
        ArgumentNullException.ThrowIfNull(config);
        ServerText.Use(config);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
