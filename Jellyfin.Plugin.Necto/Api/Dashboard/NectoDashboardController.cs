using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Mime;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Api.WireGuard;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Services.Configuration;
using Jellyfin.Plugin.Necto.Services.Dashboard;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
using Jellyfin.Plugin.Necto.Services.WireGuard;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Api.Dashboard;

/// <summary>
/// The routes of the Dashboard tool: its world map and Necto's recent activity. Jellyfin has a controller named
/// Dashboard of its own, so this one carries the plugin's name. Every route is for administrators.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("Dashboard")]
[Produces(MediaTypeNames.Application.Json)]
public class NectoDashboardController : ControllerBase
{
    private readonly IConfigurationStore _store;
    private readonly VisitorStore _visitors;
    private readonly GeoIpService _geo;
    private readonly ServerLocation _server;
    private readonly MapLocations _locations;
    private readonly TunnelSupervisor _supervisor;
    private readonly IUserManager _users;
    private readonly ActivityFeed _activity;

    /// <summary>
    /// Initializes a new instance of the <see cref="NectoDashboardController"/> class.
    /// </summary>
    /// <param name="store">Reads whether the proxy and WireGuard are on.</param>
    /// <param name="visitors">The proxy's visitor history.</param>
    /// <param name="geo">The location database.</param>
    /// <param name="server">This server's place.</param>
    /// <param name="locations">Gets the map's places ready.</param>
    /// <param name="supervisor">Runs the WireGuard tunnels.</param>
    /// <param name="users">Names the peers' owners.</param>
    /// <param name="activity">Reads Necto's activity log entries.</param>
    public NectoDashboardController(
        IConfigurationStore store,
        VisitorStore visitors,
        GeoIpService geo,
        ServerLocation server,
        MapLocations locations,
        TunnelSupervisor supervisor,
        IUserManager users,
        ActivityFeed activity)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _visitors = visitors ?? throw new ArgumentNullException(nameof(visitors));
        _geo = geo ?? throw new ArgumentNullException(nameof(geo));
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _locations = locations ?? throw new ArgumentNullException(nameof(locations));
        _supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
    }

    /// <summary>
    /// Gives the world map: the proxy's visitors, failed sign-ins, and refusals by country while the proxy is on, the
    /// WireGuard tunnel ends placed by the location database while WireGuard is on, and this server's country.
    /// </summary>
    /// <returns>The map.</returns>
    [HttpGet("Map")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<DashboardMap> Map()
    {
        var (proxyOn, wireGuardOn, wireGuard) = _store.Read(c => (c.Proxy.Enabled, c.WireGuard.Enabled, SecretJson.Copy(c.WireGuard)));
        if (proxyOn || wireGuardOn)
        {
            _locations.Prepare();
        }

        var none = new List<CountryTotal>();
        var ends = wireGuardOn
            ? PeerMap.Build(wireGuard, _supervisor.Status(), _supervisor.Blocked(), _supervisor.Refused(), id => _users.GetUserById(id)?.Username)
            : new List<PeerConnection>();
        var map = MapBuilder.Build(
            proxyOn,
            wireGuardOn,
            _server.Location,
            proxyOn ? _visitors.GetPeopleByCountry() : none,
            proxyOn ? _visitors.GetFailedByCountry() : none,
            proxyOn ? _visitors.GetBlockedByCountry() : none,
            ends,
            address => IPAddress.TryParse(address, out var ip) ? _geo.Lookup(ip) : GeoLocation.Unknown);
        map.LocationLoaded = _geo.IsLoaded;
        return Ok(map);
    }

    /// <summary>
    /// Gives Necto's newest activity log entries.
    /// </summary>
    /// <param name="limit">How many to give, up to 50. Defaults to 20.</param>
    /// <returns>The entries, newest first.</returns>
    [HttpGet("Activity")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ActivityItem>>> Activity([FromQuery] int limit = 20)
        => Ok(await _activity.RecentAsync(limit).ConfigureAwait(false));
}
