using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.WireGuard;
using Jellyfin.Plugin.Necto.Services.Configuration;
using Jellyfin.Plugin.Necto.Services.Groups;
using Jellyfin.Plugin.Necto.Services.WireGuard;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Api.WireGuard;

/// <summary>
/// The routes of the WireGuard tool: the tunnels' state and usage, the peers and connections, a peer's config file,
/// the users' allowances, and the downloaded sidecar. Every route is for administrators.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("WireGuard")]
[Produces(MediaTypeNames.Application.Json)]
public class WireGuardController : ControllerBase
{
    private readonly TunnelSupervisor _supervisor;
    private readonly UsageTracker _usage;
    private readonly WireGuardEditor _editor;
    private readonly SidecarStore _sidecar;
    private readonly SecretProtector _secrets;
    private readonly IConfigurationStore _store;
    private readonly PermissionService _permissions;
    private readonly IUserManager _users;

    /// <summary>
    /// Initializes a new instance of the <see cref="WireGuardController"/> class.
    /// </summary>
    /// <param name="supervisor">Runs the tunnels.</param>
    /// <param name="usage">Counts traffic.</param>
    /// <param name="editor">Changes peers and connections.</param>
    /// <param name="sidecar">Keeps the downloaded sidecar.</param>
    /// <param name="secrets">Decrypts keys for a config file.</param>
    /// <param name="store">Reads the settings.</param>
    /// <param name="permissions">Gives what each user may have.</param>
    /// <param name="users">Lists and names users.</param>
    public WireGuardController(
        TunnelSupervisor supervisor,
        UsageTracker usage,
        WireGuardEditor editor,
        SidecarStore sidecar,
        SecretProtector secrets,
        IConfigurationStore store,
        PermissionService permissions,
        IUserManager users)
    {
        _supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));
        _usage = usage ?? throw new ArgumentNullException(nameof(usage));
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _sidecar = sidecar ?? throw new ArgumentNullException(nameof(sidecar));
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _users = users ?? throw new ArgumentNullException(nameof(users));
    }

    /// <summary>
    /// Reads every running tunnel's state, with each peer's traffic and handshake.
    /// </summary>
    /// <returns>One status per running tunnel.</returns>
    [HttpGet("Status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<TunnelStatus>> Status() => Ok(_supervisor.Status());

    /// <summary>
    /// Reads every peer's and connection's traffic in the current hour, day, week, month, and year, which are over
    /// their data limit, and which peers their owners' groups leave out.
    /// </summary>
    /// <returns>The usage.</returns>
    [HttpGet("Usage")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<UsageResponse> Usage() => Ok(new UsageResponse
    {
        Bytes = _usage.All().ToDictionary(kv => kv.Key.ToString("N"), kv => kv.Value),
        Blocked = _supervisor.Blocked().Select(id => id.ToString("N")).ToList(),
        Refused = _supervisor.Refused().ToDictionary(kv => kv.Key.ToString("N"), kv => kv.Value.ToString())
    });

    /// <summary>
    /// Lists every tunnel end with where it connects from, for the Dashboard map: the host, its peers, and the
    /// connections.
    /// </summary>
    /// <returns>The tunnel ends, the host first.</returns>
    [HttpGet("Peers")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<PeerConnection>> Peers()
    {
        var settings = _store.Read(c => SecretJson.Copy(c.WireGuard));
        return Ok(PeerMap.Build(settings, _supervisor.Status(), _supervisor.Blocked(), _supervisor.Refused(), id => _users.GetUserById(id)?.Username));
    }

    /// <summary>
    /// Adds a peer.
    /// </summary>
    /// <param name="input">The peer's fields.</param>
    /// <returns>The stored peer, or 400 with the reason.</returns>
    [HttpPost("Peers")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<TunnelPeer> AddPeer([FromBody] PeerInput input) => Edit(() => _editor.SavePeer(null, input));

    /// <summary>
    /// Saves a peer.
    /// </summary>
    /// <param name="id">The peer.</param>
    /// <param name="input">The peer's fields.</param>
    /// <returns>The stored peer, 400 with the reason, or 404.</returns>
    [HttpPut("Peers/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<TunnelPeer> SavePeer([FromRoute] Guid id, [FromBody] PeerInput input) => Edit(() => _editor.SavePeer(id, input));

    /// <summary>
    /// Removes a peer. Its configuration stops working.
    /// </summary>
    /// <param name="id">The peer.</param>
    /// <returns>204, or 404.</returns>
    [HttpDelete("Peers/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult RemovePeer([FromRoute] Guid id) => _editor.RemovePeer(id) ? NoContent() : NotFound(new ApiError("No peer has that id."));

    /// <summary>
    /// Writes a peer's WireGuard configuration, with its private key when this server made the key pair.
    /// </summary>
    /// <param name="id">The peer.</param>
    /// <returns>The configuration as text, 404 for an unknown peer, or 409 while the host has no key or subnet.</returns>
    [HttpGet("Peers/{id:guid}/Config")]
    [Produces("text/plain")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public IActionResult PeerConfig([FromRoute] Guid id)
    {
        var settings = _store.Read(c => SecretJson.Copy(c.WireGuard));
        if (settings.Peers.FirstOrDefault(p => p.Id == id) is not { } peer)
        {
            return NotFound(new ApiError("No peer has that id."));
        }

        var text = PeerConfigWriter.Write(settings, peer, _secrets.Unprotect);
        Response.Headers.CacheControl = "no-store";
        return text is null ? Conflict(new ApiError("Turn on the host with a tunnel subnet first.")) : Content(text, "text/plain; charset=utf-8");
    }

    /// <summary>
    /// Adds a connection to another WireGuard host.
    /// </summary>
    /// <param name="input">The connection's fields.</param>
    /// <returns>The stored connection, or 400 with the reason.</returns>
    [HttpPost("Connections")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<TunnelConnection> AddConnection([FromBody] ConnectionInput input) => Edit(() => _editor.SaveConnection(null, input));

    /// <summary>
    /// Saves a connection.
    /// </summary>
    /// <param name="id">The connection.</param>
    /// <param name="input">The connection's fields.</param>
    /// <returns>The stored connection, 400 with the reason, or 404.</returns>
    [HttpPut("Connections/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<TunnelConnection> SaveConnection([FromRoute] Guid id, [FromBody] ConnectionInput input) => Edit(() => _editor.SaveConnection(id, input));

    /// <summary>
    /// Removes a connection. This server stops connecting to it.
    /// </summary>
    /// <param name="id">The connection.</param>
    /// <returns>204, or 404.</returns>
    [HttpDelete("Connections/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult RemoveConnection([FromRoute] Guid id)
        => _editor.RemoveConnection(id) ? NoContent() : NotFound(new ApiError("No connection has that id."));

    /// <summary>
    /// Lists every user with their peers and what their groups give them.
    /// </summary>
    /// <returns>One row per user.</returns>
    [HttpGet("Users")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<PeerAllowanceRow>> Users()
    {
        var peers = _store.Read(c => c.WireGuard.Peers.Select(p => p.UserId).ToList());
        return Ok(_users.GetUsers().Select(u =>
        {
            var p = _permissions.For(u);
            return new PeerAllowanceRow
            {
                UserId = u.Id,
                Name = u.Username,
                IsAdministrator = u.HasPermission(PermissionKind.IsAdministrator),
                Used = peers.Count(id => id == u.Id),
                MayHavePeers = p.WireGuard.MayHavePeers,
                MaxPeers = p.WireGuard.MaxPeers,
                DataLimit = p.WireGuard.DataLimit,
                DataPeriod = p.WireGuard.DataPeriod
            };
        }).OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>
    /// Describes the downloaded sidecar and the release Necto downloads.
    /// </summary>
    /// <returns>The sidecar's description.</returns>
    [HttpGet("Sidecar")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<SidecarInfo> Sidecar() => Ok(_sidecar.Info());

    /// <summary>
    /// Downloads wireguard-go, as the wgsidecar built on it, for this server, replacing the one kept before, and starts
    /// the tunnels with it.
    /// </summary>
    /// <param name="cancellationToken">Stops the download.</param>
    /// <returns>The sidecar's description, or 502 with the reason.</returns>
    [HttpPost("Sidecar/Download")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<SidecarInfo>> DownloadSidecar(CancellationToken cancellationToken)
    {
        try
        {
            var info = await _sidecar.DownloadAsync(cancellationToken).ConfigureAwait(false);
            await _supervisor.RestartAsync().ConfigureAwait(false);
            return Ok(info);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new ApiError(ex.Message));
        }
    }

    /// <summary>
    /// Stops every tunnel and starts them again.
    /// </summary>
    /// <returns>204 once the tunnels started.</returns>
    [HttpPost("Restart")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Restart()
    {
        await _supervisor.RestartAsync().ConfigureAwait(false);
        return NoContent();
    }

    // Runs an edit and answers with the stored record, or with why it was refused.
    private ActionResult<T> Edit<T>(Func<T> edit)
    {
        try
        {
            return Ok(edit());
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ApiError("No record has that id."));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ApiError(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiError(ex.Message));
        }
    }
}
