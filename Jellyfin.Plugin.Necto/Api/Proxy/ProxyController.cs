using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Api.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Services.Proxy.Reachability;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
using MediaBrowser.Common.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Api.Proxy;

/// <summary>
/// The routes of the Proxy tool: its settings, its routes and their certificates, its status and tests, and trusting
/// the proxy in Jellyfin. Every route is for administrators.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("Proxy")]
[Produces(MediaTypeNames.Application.Json)]
public class ProxyController : ControllerBase
{
    // The largest certificate file taken, far more than any chain needs.
    private const long MaxCertificateBytes = 256 * 1024;

    private readonly ProxyManager _proxy;
    private readonly JellyfinNetworkOrchestrator _orchestrator;
    private readonly SecretProtector _secrets;
    private readonly ActivityLogger _activity;
    private readonly GeoIpService _geo;
    private readonly ServerLocation _server;
    private readonly INetworkManager _network;
    private readonly ProxyPaths _paths;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProxyController"/> class.
    /// </summary>
    /// <param name="proxy">Runs the proxy.</param>
    /// <param name="orchestrator">Trusts the proxy in Jellyfin, and undoes it.</param>
    /// <param name="secrets">Encrypts certificate passwords.</param>
    /// <param name="activity">Writes the activity log.</param>
    /// <param name="geo">Looks up an address's country, for the self lockout check.</param>
    /// <param name="server">This server's country.</param>
    /// <param name="network">Jellyfin's LAN test.</param>
    /// <param name="paths">Where each route keeps its certificate files.</param>
    public ProxyController(ProxyManager proxy, JellyfinNetworkOrchestrator orchestrator, SecretProtector secrets, ActivityLogger activity, GeoIpService geo, ServerLocation server, INetworkManager network, ProxyPaths paths)
    {
        ArgumentNullException.ThrowIfNull(proxy);
        ArgumentNullException.ThrowIfNull(orchestrator);
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(paths);
        _geo = geo;
        _server = server;
        _network = network;
        _proxy = proxy;
        _orchestrator = orchestrator;
        _secrets = secrets;
        _activity = activity;
        _paths = paths;
    }

    /// <summary>
    /// Gives the proxy's settings: on or off, the Let's Encrypt terms, the listener and protection values, and the
    /// visitor history. The routes are left out.
    /// </summary>
    /// <returns>The settings.</returns>
    [HttpGet("Settings")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<ProxySettings> GetSettings()
    {
        var settings = Read();
        settings.Routes = new List<ProxyRoute>();
        return Ok(settings);
    }

    /// <summary>
    /// Saves the proxy's settings and brings the proxy in line. The routes stay as they are.
    /// </summary>
    /// <param name="incoming">The settings. Its routes are ignored.</param>
    /// <returns>The settings and status as saved, or 400 with every problem.</returns>
    [HttpPost("Settings")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<object>> SaveSettings([FromBody] ProxySettings incoming)
    {
        if (incoming is null)
        {
            return BadRequest(Errors(Reply(TranslationKeys.ProxyErrorNothingSent)));
        }

        var stored = Read();
        incoming.Routes = stored.Routes;
        return await CommitAsync(incoming, stored).ConfigureAwait(false);
    }

    /// <summary>
    /// Lists the routes, with each certificate password replaced by a marker.
    /// </summary>
    /// <returns>The routes and their status.</returns>
    [HttpGet("Routes")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> ListRoutes()
    {
        var settings = Read();
        return Ok(new { Routes = settings.Routes.Select(Redact).ToList(), Status = _proxy.RouteStatuses(settings) });
    }

    /// <summary>
    /// Gives the templates for a new route, under the domain the DDNS records suggest.
    /// </summary>
    /// <returns>The suggested domain and a route per template.</returns>
    [HttpGet("Templates")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> GetTemplates()
    {
        var domain = Plugin.Instance?.ReadConfiguration(c => RouteTemplates.SuggestDomain(c.Ddns.Records)) ?? string.Empty;
        return Ok(new
        {
            Domain = domain,
            Templates = new[] { RouteTemplates.Jellyfin, RouteTemplates.JellyfinLan, RouteTemplates.Blank }
                .Select(key => new { Key = key, Route = RouteTemplates.Build(key, domain) })
                .ToList()
        });
    }

    /// <summary>
    /// Adds a route and brings the proxy in line.
    /// </summary>
    /// <param name="route">The new route. Any id it carries is replaced.</param>
    /// <returns>The route as saved, or 400 with every problem.</returns>
    [HttpPost("Routes")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<object>> CreateRoute([FromBody] ProxyRoute route)
    {
        if (route is null)
        {
            return BadRequest(Errors(Reply(TranslationKeys.ProxyErrorNothingSent)));
        }

        route.Id = Guid.NewGuid().ToString("N");
        return await SaveRouteAsync(route, null).ConfigureAwait(false);
    }

    /// <summary>
    /// Saves a route and brings the proxy in line.
    /// </summary>
    /// <param name="id">The route's id.</param>
    /// <param name="route">The route's new values.</param>
    /// <returns>The route as saved, 400 with every problem, or 404 when no route has the id.</returns>
    [HttpPut("Routes/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<object>> UpdateRoute([FromRoute] string id, [FromBody] ProxyRoute route)
    {
        if (route is null)
        {
            return BadRequest(Errors(Reply(TranslationKeys.ProxyErrorNothingSent)));
        }

        var existing = Read().Routes.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.Ordinal));
        if (existing is null)
        {
            return NotFound(Errors(Reply(TranslationKeys.ProxyErrorRouteNotFound)));
        }

        route.Id = existing.Id;
        return await SaveRouteAsync(route, existing).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes a route and its certificate files. The service it forwarded to is left alone.
    /// </summary>
    /// <param name="id">The route's id.</param>
    /// <returns>The status, 400 when Jellyfin needs the route, or 404 when no route has the id.</returns>
    [HttpDelete("Routes/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<object>> RemoveRoute([FromRoute] string id)
    {
        var stored = Read();
        var existing = stored.Routes.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.Ordinal));
        if (existing is null)
        {
            return NotFound(Errors(Reply(TranslationKeys.ProxyErrorRouteNotFound)));
        }

        var incoming = ProxyRuntimeFactory.Clone(stored);
        incoming.Routes.RemoveAll(r => string.Equals(r.Id, id, StringComparison.Ordinal));
        var result = await CommitAsync(incoming, stored).ConfigureAwait(false);
        if (result.Result is OkObjectResult)
        {
            DeleteRouteFiles(existing.Id);
        }

        return result;
    }

    /// <summary>
    /// Takes a certificate for a route: a PKCS#12 file with its password, or a PEM chain with its key. The files are
    /// kept in the route's own folder, readable only by Jellyfin's account, and the route serves them from then on.
    /// </summary>
    /// <param name="id">The route's id.</param>
    /// <param name="certificate">The .pfx or PEM chain file.</param>
    /// <param name="key">The PEM private key, for a PEM chain.</param>
    /// <param name="password">The file's or key's password, if any.</param>
    /// <returns>The route as saved, 400 when the files do not load, or 404 when no route has the id.</returns>
    [HttpPost("Routes/{id}/Certificate")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<object>> UploadCertificate([FromRoute] string id, IFormFile? certificate, IFormFile? key, [FromForm] string? password)
    {
        var existing = Read().Routes.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.Ordinal));
        if (existing is null)
        {
            return NotFound(Errors(Reply(TranslationKeys.ProxyErrorRouteNotFound)));
        }

        if (certificate is null || certificate.Length == 0 || certificate.Length > MaxCertificateBytes || (key is not null && key.Length > MaxCertificateBytes))
        {
            return BadRequest(Errors(Reply(TranslationKeys.ProxyErrorChooseCertificateFile)));
        }

        if (!string.IsNullOrEmpty(password) && !_secrets.IsAvailable)
        {
            return BadRequest(Errors(CredentialEncryption.KeyStoreUnavailable));
        }

        var certBytes = await ReadAllAsync(certificate).ConfigureAwait(false);
        var keyBytes = key is null || key.Length == 0 ? null : await ReadAllAsync(key).ConfigureAwait(false);
        var pem = keyBytes is not null || LooksLikePem(certBytes);
        var problem = UploadProblem(certBytes, keyBytes, password ?? string.Empty, pem);
        if (problem is not null)
        {
            return BadRequest(Errors(Reply(TranslationKeys.ProxyErrorCertificateUnloadable, problem)));
        }

        var slot = _paths.ForRoute(existing.Id);
        Directory.CreateDirectory(slot.Directory);
        SecretFile.Write(slot.UploadedFile, certBytes);
        if (keyBytes is not null)
        {
            SecretFile.Write(slot.UploadedKeyFile, keyBytes);
        }
        else if (System.IO.File.Exists(slot.UploadedKeyFile))
        {
            System.IO.File.Delete(slot.UploadedKeyFile);
        }

        var route = Clone(existing);
        route.Certificate.Mode = pem ? CertificateMode.PemFiles : CertificateMode.Pkcs12File;
        route.Certificate.Path = slot.UploadedFile;
        route.Certificate.KeyPath = pem ? (keyBytes is null ? slot.UploadedFile : slot.UploadedKeyFile) : string.Empty;
        route.Certificate.Password = string.IsNullOrEmpty(password) ? string.Empty : _secrets.Protect(password);
        return await SaveRouteAsync(route, existing, passwordResolved: true).ConfigureAwait(false);
    }

    /// <summary>
    /// Orders a new Let's Encrypt certificate for a route now, past the usual timing rules.
    /// </summary>
    /// <param name="id">The route's id.</param>
    /// <returns>Whether one was issued, what to tell the administrator, and the route's status.</returns>
    [HttpPost("Routes/{id}/Certificate/Request")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<object>> RequestCertificate([FromRoute] string id)
    {
        _activity.Log(ServerText.Get(TranslationKeys.ProxyActivityCertificateRequested), "Necto.Proxy.CertificateRequested", ServerText.Format(TranslationKeys.ProxyActivityCertificateRequestedOverview, ActivityUser.NameOf(User)), userId: ActivityUser.Of(User));
        var (ok, message) = await _proxy.RequestCertificateAsync(id, CancellationToken.None).ConfigureAwait(false);
        return Ok(new { Ok = ok, Message = message, Status = _proxy.RouteStatuses(Read()) });
    }

    /// <summary>
    /// Sends one request through the running proxy for a route's name and reports whether its service answered.
    /// </summary>
    /// <param name="id">The route's id.</param>
    /// <returns>Whether the service answered, and what went wrong when it did not.</returns>
    [HttpPost("Routes/{id}/Test")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<object>> TestRoute([FromRoute] string id)
    {
        var problem = await _proxy.TestRouteAsync(id, HttpContext?.RequestAborted ?? CancellationToken.None).ConfigureAwait(false);
        return Ok(new { Ok = problem is null, Message = problem ?? string.Empty });
    }

    /// <summary>
    /// Describes the proxy, with the public name looked up again when the last answer is old.
    /// </summary>
    /// <returns>The status.</returns>
    [HttpGet("Status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProxyStatus>> GetStatus()
        => Ok(await _proxy.GetStatusAsync(_orchestrator.State, HttpContext?.RequestAborted ?? CancellationToken.None).ConfigureAwait(false));

    /// <summary>
    /// Asks a few places on the internet to connect to the first internet route's HTTPS port.
    /// </summary>
    /// <returns>The result.</returns>
    [HttpPost("Reachability")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<ReachabilityResult>> CheckReachability()
        => Ok(await _proxy.CheckReachabilityAsync(HttpContext?.RequestAborted ?? CancellationToken.None).ConfigureAwait(false));

    /// <summary>
    /// Lists the names the DDNS records keep pointed at this server, as choices for a route's host name.
    /// </summary>
    /// <returns>The names.</returns>
    [HttpGet("DdnsHostnames")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<List<DdnsHostname>> GetDdnsHostnames()
        => Ok(Plugin.Instance?.ReadConfiguration(c => DdnsHostnames.From(c.Ddns.Records)) ?? new List<DdnsHostname>());

    /// <summary>
    /// Gives the defaults in effect, and both sets the server's memory chooses between.
    /// </summary>
    /// <returns>The defaults.</returns>
    [HttpGet("Defaults")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<ProxyDefaults> GetDefaults() => Ok(new ProxyDefaults
    {
        Values = ProxyOptions.Defaults,
        Standard = ProxyOptions.Standard,
        LowMemory = ProxyOptions.LowMemory,
        IsLowMemory = ProxyOptions.IsLowMemory,
        MemoryMb = ProxyOptions.MemoryBytes / (1024 * 1024),
        LowMemoryBelowMb = ProxyOptions.LowMemoryBelowBytes / (1024 * 1024)
    });

    /// <summary>
    /// Sets Jellyfin to trust the proxy, after a test copy proves the proxy reaches this Jellyfin.
    /// </summary>
    /// <param name="cancellationToken">Cancels the test.</param>
    /// <returns>The outcome.</returns>
    [HttpPost("Trust")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<TakeoverResult>> TrustProxy(CancellationToken cancellationToken)
        => Ok(await _orchestrator.ApplyAsync(Read(), _proxy, cancellationToken, ActivityUser.Of(User)).ConfigureAwait(false));

    /// <summary>
    /// Puts Jellyfin's network settings back as they were before it trusted the proxy.
    /// </summary>
    /// <returns>The outcome.</returns>
    [HttpPost("Trust/Undo")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<TakeoverResult> UndoTrust()
        => Ok(_orchestrator.Restore(ServerText.Format(TranslationKeys.ProxyActivityUndoneBy, ActivityUser.NameOf(User)), restart: true, ActivityUser.Of(User)));

    /// <summary>
    /// Gives the root a device installs to trust the self-signed certificates.
    /// </summary>
    /// <returns>The root, or 404 while no route uses a self-signed certificate.</returns>
    [HttpGet("Certificate/Root")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<RootCertificate> GetRootCertificate()
        => _proxy.SelfSignedRoot is { } root ? Ok(root) : NotFound(Errors(Reply(TranslationKeys.ProxyErrorNoSelfSignedRoute)));

    /// <summary>
    /// Stops the proxy and starts it again. While Jellyfin listens on this machine only, a working proxy is not
    /// stopped, since nothing else would reach Jellyfin if it could not bind its ports again.
    /// </summary>
    /// <returns>The status.</returns>
    [HttpPost("Restart")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProxyStatus>> RestartProxy()
    {
        if (!(_proxy.IsRunning && _orchestrator.IsBoundToLoopback))
        {
            await _proxy.StopAsync().ConfigureAwait(false);
        }

        await _proxy.ApplyAsync(CancellationToken.None).ConfigureAwait(false);
        return Ok(_proxy.GetStatus(_orchestrator.State));
    }

    /// <summary>
    /// Resolves a kept certificate password, checks a route against the others, and saves it.
    /// </summary>
    /// <param name="route">The route to save.</param>
    /// <param name="existing">The route as stored, or <c>null</c> for a new one.</param>
    /// <param name="passwordResolved">Whether the password is already in its stored form, as after an upload.</param>
    /// <returns>The route and status, or 400 with every problem.</returns>
    private async Task<ActionResult<object>> SaveRouteAsync(ProxyRoute route, ProxyRoute? existing, bool passwordResolved = false)
    {
        ProxySettingsValidator.NormalizeRoute(route);
        route.Id = existing?.Id ?? route.Id;
        if (XmlText.FindInvalid(route) is { } bad)
        {
            return BadRequest(Errors(Reply(TranslationKeys.DdnsErrorInvalidCharacter, bad)));
        }

        if (!passwordResolved)
        {
            if (SecretProtector.IsProtected(route.Certificate.Password))
            {
                return BadRequest(Errors(Reply(TranslationKeys.ProxyErrorEncryptedCertificatePassword)));
            }

            // A new certificate password could only be stored in the clear without the key store, which is never done.
            if (!_secrets.IsAvailable && !string.IsNullOrEmpty(route.Certificate.Password)
                && !string.Equals(route.Certificate.Password, SecretProtector.KeptSentinel, StringComparison.Ordinal))
            {
                return BadRequest(Errors(CredentialEncryption.KeyStoreUnavailable));
            }

            // A kept password belongs to the file it was entered for. Switching source or file drops it, so an old
            // .pfx password is never tried against a new, unencrypted key.
            var sameFile = existing is not null
                && route.Certificate.Mode == existing.Certificate.Mode
                && string.Equals(route.Certificate.Path, existing.Certificate.Path, StringComparison.Ordinal)
                && (route.Certificate.Mode != CertificateMode.PemFiles || string.Equals(route.Certificate.KeyPath, existing.Certificate.KeyPath, StringComparison.Ordinal));
            route.Certificate.Password = _secrets.ResolveIncoming(route.Certificate.Password, sameFile ? existing!.Certificate.Password : string.Empty);
        }

        var stored = Read();
        var errors = ProxySettingsValidator.ValidateRoute(route, stored.Routes, ServerText.LanguageOf(Request));
        if (errors.Count > 0)
        {
            return BadRequest(new ApiError(string.Join(" ", errors)));
        }

        var incoming = ProxyRuntimeFactory.Clone(stored);
        var index = incoming.Routes.FindIndex(r => string.Equals(r.Id, route.Id, StringComparison.Ordinal));
        if (index >= 0)
        {
            incoming.Routes[index] = route;
        }
        else
        {
            incoming.Routes.Add(route);
        }

        var result = await CommitAsync(incoming, stored).ConfigureAwait(false);
        return result.Result is OkObjectResult ? Ok(new { Route = Redact(route), Status = _proxy.RouteStatuses(Read()) }) : result;
    }

    /// <summary>
    /// Checks the whole settings, saves them, and brings the proxy and Jellyfin's network settings in line. A
    /// save that would stop a working proxy while Jellyfin listens on this machine only is undone and refused.
    /// </summary>
    /// <param name="incoming">The settings to save, routes included.</param>
    /// <param name="stored">The settings in effect.</param>
    /// <returns>The settings and status, or 400 with every problem.</returns>
    private async Task<ActionResult<object>> CommitAsync(ProxySettings incoming, ProxySettings stored)
    {
        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            return NotFound();
        }

        incoming.Advanced ??= new ProxyAdvancedSettings();
        incoming.Visitors ??= new VisitorSettings();
        if (XmlText.FindInvalid(incoming) is { } bad)
        {
            return BadRequest(Errors(Reply(TranslationKeys.DdnsErrorInvalidCharacter, bad)));
        }

        ProxySettingsValidator.Normalize(incoming);
        var options = ProxyOptions.From(incoming.Advanced);
        var errors = ProxySettingsValidator.Validate(incoming, ServerText.LanguageOf(Request));
        if (!incoming.Enabled && _orchestrator.State.Snapshot is not null)
        {
            errors.Add(Reply(TranslationKeys.ProxyErrorUndoBeforeOff));
        }

        if (SelfLockout(incoming) is { } lockout)
        {
            errors.Add(lockout);
        }

        if (errors.Count > 0)
        {
            return BadRequest(new ApiError(string.Join(" ", errors)));
        }

        // Binding Jellyfin to this machine is part of trusting the proxy, so while it is trusted a change to it is
        // applied the same way, with Jellyfin restarting once. Turning it off is applied first, since it needs
        // nothing of the proxy. Turning it on is applied last, once the proxy runs with the new settings.
        string? restart = null;
        var user = ActivityUser.Of(User);
        var loopbackNow = options.JellyfinLoopbackOnly;
        var loopbackChanged = ProxyOptions.From(stored.Advanced).JellyfinLoopbackOnly != loopbackNow;
        if (loopbackChanged && !loopbackNow)
        {
            var result = await _orchestrator.ApplyLoopbackChangeAsync(incoming, _proxy, user).ConfigureAwait(false);
            if (!result.Success)
            {
                return BadRequest(Errors(result.Message));
            }

            restart = result.Restarting ? Reply(TranslationKeys.ProxyMessageJellyfinRestarting) : null;
        }

        var wasRunning = _proxy.IsRunning;
        plugin.MutateConfiguration(c =>
        {
            c.Proxy = incoming;
            return true;
        });
        await _proxy.ApplyAsync(CancellationToken.None).ConfigureAwait(false);
        if (incoming.Enabled && wasRunning && !_proxy.IsRunning && _orchestrator.IsBoundToLoopback)
        {
            var why = await _proxy.CheckRunningAsync(CancellationToken.None).ConfigureAwait(false);
            await PutBackAsync(plugin, stored).ConfigureAwait(false);
            return BadRequest(Errors(Reply(TranslationKeys.ProxyErrorProxyCouldNotStart) + " " + why));
        }

        if (loopbackChanged && loopbackNow)
        {
            var result = await _orchestrator.ApplyLoopbackChangeAsync(incoming, _proxy, user).ConfigureAwait(false);
            if (!result.Success)
            {
                await PutBackAsync(plugin, stored).ConfigureAwait(false);
                return BadRequest(Errors(result.Message));
            }

            restart = result.Restarting ? Reply(TranslationKeys.ProxyMessageJellyfinRestarting) : null;
        }
        else if (!loopbackChanged)
        {
            // New names change the address Jellyfin gives its clients. Jellyfin reads that again when its network
            // settings are saved, so this needs no restart.
            _orchestrator.UpdatePublishedAddresses(incoming, user);
        }

        var changed = ChangedSections(stored, incoming);
        if (changed.Count > 0)
        {
            _activity.Log(
                ServerText.Get(TranslationKeys.ProxyActivityProxySettingsChanged),
                "Necto.Proxy.ProxySettingsChanged",
                ServerText.Format(incoming.Enabled ? TranslationKeys.ProxyActivityChangedProxyOn : TranslationKeys.ProxyActivityChangedProxyOff, ActivityUser.NameOf(User), string.Join(", ", changed)),
                userId: user);
        }

        var saved = Read();
        saved.Routes = saved.Routes.Select(Redact).ToList();
        return Ok(new { Settings = saved, Status = _proxy.GetStatus(_orchestrator.State), Restart = restart });
    }

    // Puts the settings from before a refused save back, and the proxy with them.
    private async Task PutBackAsync(Plugin plugin, ProxySettings stored)
    {
        plugin.MutateConfiguration(c =>
        {
            c.Proxy = stored;
            return true;
        });
        await _proxy.ApplyAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Names what a save changed, in the page's own words, for the activity entry. An unchanged save names nothing.
    /// </summary>
    /// <param name="before">The settings before.</param>
    /// <param name="after">The settings after.</param>
    /// <returns>The changed parts.</returns>
    internal static List<string> ChangedSections(ProxySettings before, ProxySettings after)
    {
        static string Json<T>(T value) => System.Text.Json.JsonSerializer.Serialize(value);
        var changed = new List<string>();
        if (before.Enabled != after.Enabled)
        {
            changed.Add(ServerText.Get(after.Enabled ? TranslationKeys.ProxyActivityRemoteAccessOn : TranslationKeys.ProxyActivityRemoteAccessOff));
        }

        if (Json(before.Routes.Select(r => new { r.Id, r.Enabled, r.Host, r.Upstream, r.LocalOnly })) != Json(after.Routes.Select(r => new { r.Id, r.Enabled, r.Host, r.Upstream, r.LocalOnly })))
        {
            changed.Add(ServerText.Get(TranslationKeys.ProxyActivitySectionAddresses));
        }

        if (before.AcceptTerms != after.AcceptTerms || Json(before.Routes.Select(r => r.Certificate)) != Json(after.Routes.Select(r => r.Certificate)))
        {
            changed.Add(ServerText.Get(TranslationKeys.ProxyActivitySectionCertificate));
        }

        if (Json(before.Advanced) != Json(after.Advanced) || Json(before.Visitors) != Json(after.Visitors))
        {
            changed.Add(ServerText.Get(TranslationKeys.ProxyActivitySectionAdvanced));
        }

        return changed;
    }

    // Blocking never applies to the LAN, but an administrator saving from the internet could cut themselves off.
    private string? SelfLockout(ProxySettings settings)
    {
        var options = ProxyOptions.From(settings.Advanced);
        var client = HttpContext?.Connection.RemoteIpAddress;
        if (!settings.Enabled || client is null || (options.BlockedAddresses.Count == 0 && !CountryRules.NeedsDatabase(options)))
        {
            return null;
        }

        var ip = NetworkClassifier.Normalize(client);
        if (BlockGuard.IsExempt(ip, _network, NetworkClassifier.ParseNetworks(options.TrustedNetworks)))
        {
            return null;
        }

        if (NetworkClassifier.ParseNetworks(options.BlockedAddresses).Any(n => n.Contains(ip)))
        {
            return Reply(TranslationKeys.ProxyErrorBlocksOwnAddress, ip);
        }

        if (!CountryRules.NeedsDatabase(options))
        {
            return null;
        }

        var location = _geo.Lookup(ip);
        if (CountryRules.Allows(options, _geo.IsLoaded, location.CountryCode, _server.Location.CountryCode))
        {
            return null;
        }

        return location.Country.Length > 0
            ? Reply(TranslationKeys.ProxyErrorCountryBlocksYou, location.Country)
            : Reply(TranslationKeys.ProxyErrorCountryBlocksYouUnknown);
    }

    /// <summary>
    /// Checks that uploaded files make a certificate with its private key.
    /// </summary>
    /// <param name="certificate">The .pfx file, or the PEM chain.</param>
    /// <param name="key">The PEM key, or <c>null</c> when the chain holds it or the file is a .pfx.</param>
    /// <param name="password">The password, empty for none.</param>
    /// <param name="pem">Whether the files are PEM.</param>
    /// <returns>Why they do not load, or <c>null</c> when they do.</returns>
    internal static string? UploadProblem(byte[] certificate, byte[]? key, string password, bool pem)
    {
        try
        {
            if (pem)
            {
                var chain = System.Text.Encoding.UTF8.GetString(certificate);
                var keyText = key is null ? chain : System.Text.Encoding.UTF8.GetString(key);
                using var cert = string.IsNullOrEmpty(password)
                    ? X509Certificate2.CreateFromPem(chain, keyText)
                    : X509Certificate2.CreateFromEncryptedPem(chain, keyText, password);
                return cert.HasPrivateKey ? null : "no private key";
            }

            var all = X509CertificateLoader.LoadPkcs12Collection(certificate, string.IsNullOrEmpty(password) ? null : password);
            var keyed = all.Any(c => c.HasPrivateKey);
            foreach (var c in all)
            {
                c.Dispose();
            }

            return keyed ? null : "no private key";
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            return ex.Message;
        }
    }

    private static bool LooksLikePem(byte[] bytes)
        => System.Text.Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 4096)).Contains("-----BEGIN", StringComparison.Ordinal);

    private static async Task<byte[]> ReadAllAsync(IFormFile file)
    {
        using var stream = new MemoryStream();
        await file.CopyToAsync(stream).ConfigureAwait(false);
        return stream.ToArray();
    }

    // The files go with the route. A folder that cannot be removed now is left for the administrator, and logged nowhere
    // sensitive, since it only ever holds this route's own certificate.
    private void DeleteRouteFiles(string routeId)
    {
        try
        {
            var slot = _paths.ForRoute(routeId);
            if (Directory.Exists(slot.Directory))
            {
                Directory.Delete(slot.Directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // The route is gone either way, and a leftover folder is never read again.
            _ = ex;
        }
    }

    private static ProxySettings Read()
    {
        var settings = Plugin.Instance?.ReadConfiguration(c => ProxyRuntimeFactory.Clone(c.Proxy)) ?? new ProxySettings();
        ProxySettingsValidator.Normalize(settings);
        return settings;
    }

    private static ProxyRoute Clone(ProxyRoute route)
        => System.Text.Json.JsonSerializer.Deserialize<ProxyRoute>(System.Text.Json.JsonSerializer.Serialize(route)) ?? new ProxyRoute();

    private static ProxyRoute Redact(ProxyRoute route)
    {
        var copy = Clone(route);
        copy.Certificate.Password = SecretProtector.HasSecret(route.Certificate.Password) ? SecretProtector.KeptSentinel : string.Empty;
        return copy;
    }

    private string Reply(string key, params object?[] args) => ServerText.Reply(Request, key, args);

    private static ApiError Errors(string error) => new(error);
}
