using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Necto.Services.Dashboard;

/// <summary>
/// Gets the Dashboard map's places ready. It loads the location database when the map needs it and it is not
/// loaded, and looks up this server's own place while it is not known. Each starts in the background, at most
/// once every few minutes.
/// </summary>
[PluginService(ServiceLifetime.Singleton)]
public sealed class MapLocations
{
    private static readonly TimeSpan _retry = TimeSpan.FromMinutes(10);

    private readonly GeoIpService _geo;
    private readonly ServerLocation _server;
    private readonly TimeProvider _time;
    private readonly ILogger<MapLocations> _logger;
    private readonly RetryGate _database = new(_retry);
    private readonly RetryGate _serverLookup = new(_retry);

    /// <summary>
    /// Initializes a new instance of the <see cref="MapLocations"/> class.
    /// </summary>
    /// <param name="geo">The location database.</param>
    /// <param name="server">This server's place.</param>
    /// <param name="time">The clock, for how often each starts.</param>
    /// <param name="logger">The logger.</param>
    public MapLocations(GeoIpService geo, ServerLocation server, TimeProvider time, ILogger<MapLocations> logger)
    {
        _geo = geo ?? throw new ArgumentNullException(nameof(geo));
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Starts whatever the map is missing, without waiting for it. The next load of the map shows the result.
    /// </summary>
    public void Prepare()
    {
        var detail = VisitorService.RequiredLocation;
        if (detail == LocationDetail.Off)
        {
            return;
        }

        var now = _time.GetUtcNow();
        if (!_geo.IsLoaded)
        {
            if (_database.TryClaim(now))
            {
                _ = Task.Run(() => _geo.EnsureAsync(detail, CancellationToken.None));
            }

            return;
        }

        if (_server.Location.CountryCode.Length == 0 && _serverLookup.TryClaim(now))
        {
            _ = Task.Run(LookUpServerAsync);
        }
    }

    private async Task LookUpServerAsync()
    {
        try
        {
            await _server.RefreshAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The address lookup asks outside services, which may be down. The map leaves the server out until the next try.
            _logger.LogDebug(ex, "Could not look up this server's place for the Dashboard map");
        }
    }
}
