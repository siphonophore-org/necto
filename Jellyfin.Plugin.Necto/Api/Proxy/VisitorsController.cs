using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Siphonophore.Stolon;
using MediaBrowser.Common.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Hosting;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Services.Proxy.Reachability;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
using Jellyfin.Plugin.Necto.Api.Ddns;

namespace Jellyfin.Plugin.Necto.Api.Proxy;

/// <summary>
/// Administrative endpoints backing the history on the Dashboard tab and its settings on the Advanced tab. This
/// is personal data, so every endpoint requires an administrator.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("Proxy")]
[Produces("application/json")]
public class VisitorsController : ControllerBase
{
    private const int RowsShown = 500;

    private readonly VisitorStore _store;
    private readonly VisitorService _people;
    private readonly GeoIpService _geo;
    private readonly ActivityLogger _activity;
    private readonly ProxyManager _proxy;
    private readonly ServerLocation _server;
    private readonly BanList _bans;
    private readonly INetworkManager _network;
    private readonly AccessRecorder _access;
    private readonly BlockFilter _blocks;

    /// <summary>
    /// Initializes a new instance of the <see cref="VisitorsController"/> class.
    /// </summary>
    /// <param name="store">The history store.</param>
    /// <param name="people">The People service.</param>
    /// <param name="geo">The location lookup.</param>
    /// <param name="activity">The activity log writer.</param>
    /// <param name="proxy">The proxy manager, to apply block list changes.</param>
    /// <param name="server">The country this server is in.</param>
    /// <param name="bans">The addresses banned for failed sign-ins.</param>
    /// <param name="network">Jellyfin's network manager, for the LAN test.</param>
    /// <param name="access">The proxy's buffered visits and failures.</param>
    /// <param name="blocks">The proxy's buffered refusals.</param>
    public VisitorsController(VisitorStore store, VisitorService people, GeoIpService geo, ActivityLogger activity, ProxyManager proxy, ServerLocation server, BanList bans, INetworkManager network, AccessRecorder access, BlockFilter blocks)
    {
        _access = access;
        _blocks = blocks;
        _bans = bans;
        _proxy = proxy;
        _server = server;
        _network = network;
        _store = store;
        _people = people;
        _geo = geo;
        _activity = activity;
    }

    /// <summary>
    /// Returns what each address did, the country totals for the map, the settings and the location database
    /// status.
    /// </summary>
    /// <returns>The visitors, the totals by country for the map, this server's location, the settings and the location database status.</returns>
    /// <param name="type">Only this kind of activity, or empty for every kind.</param>
    /// <param name="place">A two-letter country code, "local" for your network, or empty for anywhere.</param>
    [HttpGet("Visitors")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> Get([FromQuery] ActivityType? type = null, [FromQuery] string? place = null)
    {
        var options = Plugin.Instance?.ReadConfiguration(c => ProxyOptions.From(c.Proxy.Advanced)) ?? ProxyOptions.Defaults;
        var blocked = NetworkClassifier.ParseNetworks(options.BlockedAddresses);
        var trusted = NetworkClassifier.ParseNetworks(options.TrustedNetworks);
        var rows = _store.GetActivity(RowsShown, type, place);
        // Country rules only refuse anyone while the proxy is on.
        var proxyOn = Plugin.Instance?.ReadConfiguration(c => c.Proxy.Enabled) ?? false;
        var rulesOn = proxyOn && CountryRules.NeedsDatabase(options) && _geo.IsLoaded;
        var serverCountry = _server.Location.CountryCode;
        var banned = _bans.Active();
        var bannedIps = banned.Select(b => b.Ip).ToHashSet(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            // Seen and refused IPv6 visitors are kept as their /64, named by its first address.
            IPAddress? parsed = IPAddress.TryParse(row.Ip, out var single) ? single
                : NetworkClassifier.TryParseNetwork(row.Ip, out var network) ? network.BaseAddress : null;
            if (parsed is null)
            {
                row.Exempt = true;
                continue;
            }

            var ip = NetworkClassifier.Normalize(parsed);
            row.Exempt = row.IsLocal || BlockGuard.IsExempt(ip, _network, trusted);
            var rowTarget = BlockGuard.TryParseTarget(row.Ip, out var parsedTarget) ? parsedTarget : BlockGuard.Single(ip);
            row.Listed = options.BlockedAddresses.Any(e => BlockGuard.NamesOnly(e, rowTarget));
            // A ban on the /56 or /48 holding the address counts too.
            row.Banned = bannedIps.Contains(NetworkClassifier.GroupKey(ip)) || _bans.BanFor(ip) is not null;
            row.Covered = blocked.Any(n => n.Contains(ip));

            // Your network and trusted networks are never refused by country, so they are never "blocked" by it.
            row.CountryBlocked = rulesOn && !row.Exempt && row.CountryCode.Length > 0
                && !CountryRules.Allows(options, databaseLoaded: true, row.CountryCode, serverCountry);
        }

        return Ok(new
        {
            Activity = rows,
            Banned = banned,
            PeopleByCountry = _store.GetPeopleByCountry(),
            FailedByCountry = _store.GetFailedByCountry(),
            BlockedByCountry = _store.GetBlockedByCountry(),
            Settings = VisitorService.Settings,
            Server = _server.Location,
            VisitorService.Recording,
            Location = _geo.GetStatus(VisitorService.RequiredLocation),
            GeoIpService.Attribution
        });
    }

    /// <summary>
    /// Adds an internet address to the block list and applies it.
    /// </summary>
    /// <param name="target">The address.</param>
    /// <returns>An empty answer, or why it was refused.</returns>
    [HttpPost("Visitors/Block")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> Block([FromBody] VisitorTarget target)
    {
        var ip = target?.Value ?? string.Empty;
        return await BlockCoreAsync(ip).ConfigureAwait(false);
    }

    private async Task<ActionResult> BlockCoreAsync(string ip)
    {
        // An IPv6 visitor is kept in the history as its /64, which is blocked whole.
        var plugin = Plugin.Instance;
        if (plugin is null || !BlockGuard.TryParseTarget(ip, out var target))
        {
            return Refuse(ServerText.Reply(Request, TranslationKeys.ProxyErrorNotAnAddress));
        }

        var address = target.BaseAddress;
        var trusted = plugin.ReadConfiguration(c => NetworkClassifier.ParseNetworks(c.Proxy.Advanced.TrustedNetworks));
        if (BlockGuard.IsExempt(address, _network, trusted))
        {
            return Refuse(ServerText.Reply(Request, TranslationKeys.ProxyTextNeverBlocked));
        }

        var client = HttpContext?.Connection.RemoteIpAddress;
        if (client is not null && target.Contains(NetworkClassifier.Normalize(client)))
        {
            return Refuse(ServerText.Reply(Request, TranslationKeys.ProxyErrorOwnAddress));
        }

        var text = BlockGuard.Text(target);
        var added = false;
        plugin.MutateConfiguration(c =>
        {
            var list = c.Proxy.Advanced.BlockedAddresses;
            added = !list.Any(e => BlockGuard.NamesOnly(e, target));
            if (added)
            {
                list.Add(text);
            }

            return added;
        });

        // Already blocked, by a second click or another page: nothing changed, so nothing to log or apply.
        if (!added)
        {
            return Done();
        }

        _activity.Log(ServerText.Get(TranslationKeys.ProxyActivityAddressBlocked), "Necto.Proxy.AddressBlocked", ServerText.Format(TranslationKeys.ProxyActivityAddressBlockedOverview, ActivityUser.NameOf(User), text), userId: ActivityUser.Of(User));
        await _proxy.ApplyAsync(CancellationToken.None).ConfigureAwait(false);
        return Done();
    }

    /// <summary>
    /// Removes an address from the block list and applies it. A range that covers it is left alone.
    /// </summary>
    /// <param name="target">The address.</param>
    /// <returns>An empty answer, or why it was refused.</returns>
    [HttpPost("Visitors/Unblock")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> Unblock([FromBody] VisitorTarget target)
    {
        var ip = target?.Value ?? string.Empty;
        return await UnblockCoreAsync(ip).ConfigureAwait(false);
    }

    private async Task<ActionResult> UnblockCoreAsync(string ip)
    {
        var plugin = Plugin.Instance;
        if (plugin is null || !BlockGuard.TryParseTarget(ip, out var target))
        {
            return Refuse(ServerText.Reply(Request, TranslationKeys.ProxyErrorNotAnAddress));
        }

        var address = BlockGuard.Text(target);
        var removed = 0;
        plugin.MutateConfiguration(c =>
        {
            removed = c.Proxy.Advanced.BlockedAddresses.RemoveAll(e => BlockGuard.NamesOnly(e, target));
            return removed > 0;
        });
        if (removed == 0)
        {
            return Refuse(ServerText.Reply(Request, TranslationKeys.ProxyErrorNotListed));
        }

        _activity.Log(ServerText.Get(TranslationKeys.ProxyActivityAddressUnblocked), "Necto.Proxy.AddressUnblocked", ServerText.Format(TranslationKeys.ProxyActivityAddressUnblockedOverview, ActivityUser.NameOf(User), address), userId: ActivityUser.Of(User));
        await _proxy.ApplyAsync(CancellationToken.None).ConfigureAwait(false);
        return Done();
    }

    /// <summary>
    /// Saves the People settings and loads or downloads the location database they need.
    /// </summary>
    /// <param name="incoming">The settings.</param>
    /// <returns>The saved settings.</returns>
    [HttpPost("Visitors/Settings")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<VisitorSettings> SaveSettings([FromBody] VisitorSettings incoming)
    {
        var plugin = Plugin.Instance;
        if (incoming is null || plugin is null)
        {
            return BadRequest();
        }

        if (!Enum.IsDefined(incoming.Location))
        {
            return Refuse(ServerText.Reply(Request, TranslationKeys.ProxyErrorUnknownLocation));
        }

        incoming.HistoryDays = Math.Clamp(incoming.HistoryDays, 1, 3650);
        VisitorSettings? before = null;
        plugin.MutateConfiguration(c =>
        {
            before = c.Proxy.Visitors;
            c.Proxy.Visitors = incoming;
            return true;
        });

        // The Advanced page saves these with every save, so only an actual change is an event.
        if (before is null || before.Enabled != incoming.Enabled || before.HistoryDays != incoming.HistoryDays || before.Location != incoming.Location)
        {
            var overview = incoming.Enabled
                ? ServerText.Format(TranslationKeys.ProxyActivityHistoryOn, ActivityUser.NameOf(User), incoming.HistoryDays, incoming.Location)
                : ServerText.Format(TranslationKeys.ProxyActivityHistoryOff, ActivityUser.NameOf(User));
            _activity.Log(ServerText.Get(TranslationKeys.ProxyActivityHistorySettingsChanged), "Necto.Proxy.HistorySettingsChanged", overview, userId: ActivityUser.Of(User));
        }

        // Loading or downloading the database can take a while; the status shows progress.
        _ = Task.Run(() => _geo.EnsureAsync(VisitorService.RequiredLocation, CancellationToken.None));
        _people.Prune();
        return Ok(incoming);
    }

    /// <summary>
    /// Downloads the newest location database now.
    /// </summary>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>The location database status.</returns>
    [HttpPost("Visitors/Location/Update")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<GeoIpStatus>> UpdateLocation(CancellationToken cancellationToken)
    {
        var detail = VisitorService.RequiredLocation;
        await _geo.UpdateAsync(detail, cancellationToken).ConfigureAwait(false);
        return Ok(_geo.GetStatus(detail));
    }

    /// <summary>
    /// Lifts the ban on an address.
    /// </summary>
    /// <param name="target">The address.</param>
    /// <returns>An empty answer, or why it was refused.</returns>
    [HttpPost("Visitors/Unban")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult Unban([FromBody] VisitorTarget target)
    {
        var ip = target?.Value ?? string.Empty;
        return UnbanCore(ip);
    }

    private ActionResult UnbanCore(string ip)
    {
        // A ban is held per IPv4 address, or per IPv6 /64, /56 or /48. An address or a /64, as a visitor's row
        // shows it, lifts every ban that holds it, its own and any on a wider network, so the visitor really can
        // connect again. A /56 or /48 lifts that ban alone.
        List<string> lifted;
        if (IPAddress.TryParse(ip, out var parsed))
        {
            lifted = _bans.UnbanAll(NetworkClassifier.Normalize(parsed));
        }
        else if (NetworkClassifier.TryParseNetwork(ip, out var network) && network.PrefixLength == 64)
        {
            lifted = _bans.UnbanAll(network.BaseAddress);
        }
        else if (NetworkClassifier.TryParseNetwork(ip, out var wide) && wide.PrefixLength is 56 or 48
            && NetworkClassifier.PrefixKey(wide.BaseAddress, wide.PrefixLength) is { } wideKey)
        {
            lifted = _bans.Unban(wideKey) ? new List<string> { wideKey } : new List<string>();
        }
        else
        {
            return Refuse(ServerText.Reply(Request, TranslationKeys.ProxyErrorNotAnAddress));
        }

        if (lifted.Count == 0)
        {
            return Refuse(ServerText.Reply(Request, TranslationKeys.ProxyErrorNotBanned));
        }

        _activity.Log(
            ServerText.Get(lifted.Count == 1 ? TranslationKeys.ProxyActivityBanLifted : TranslationKeys.ProxyActivityBansLifted),
            "Necto.Proxy.BanLifted",
            ServerText.Format(TranslationKeys.ProxyActivityBanLiftedOverview, ActivityUser.NameOf(User), ServerText.List(lifted)),
            userId: ActivityUser.Of(User));
        return Done();
    }

    /// <summary>
    /// Lets a country through the country rules and applies it: removed from a block list, or added to the
    /// countries allowed.
    /// </summary>
    /// <param name="target">The two-letter country code.</param>
    /// <returns>An empty answer, or why it was refused.</returns>
    [HttpPost("Visitors/UnblockCountry")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> UnblockCountry([FromBody] VisitorTarget target)
    {
        var code = target?.Value ?? string.Empty;
        return await UnblockCountryCoreAsync(code).ConfigureAwait(false);
    }

    private async Task<ActionResult> UnblockCountryCoreAsync(string code)
    {
        var plugin = Plugin.Instance;
        var country = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (plugin is null || country.Length != 2 || !country.All(char.IsAsciiLetterUpper))
        {
            return Refuse(ServerText.Reply(Request, TranslationKeys.ProxyErrorNotCountryCode));
        }

        var changed = false;
        plugin.MutateConfiguration(c =>
        {
            var advanced = c.Proxy.Advanced;
            switch (ProxyOptions.From(advanced).CountryMode)
            {
                case CountryRuleMode.Block:
                    changed = advanced.Countries.RemoveAll(x => string.Equals(x, country, StringComparison.OrdinalIgnoreCase)) > 0;
                    break;
                case CountryRuleMode.AllowOnly:
                case CountryRuleMode.ServerCountry:
                    if (!advanced.Countries.Contains(country, StringComparer.OrdinalIgnoreCase))
                    {
                        advanced.Countries.Add(country);
                        changed = true;
                    }

                    break;
            }

            return changed;
        });
        if (!changed)
        {
            return Refuse(ServerText.Reply(Request, TranslationKeys.ProxyErrorCountryNotBlocked));
        }

        _activity.Log(ServerText.Get(TranslationKeys.ProxyActivityCountryUnblocked), "Necto.Proxy.CountryUnblocked", ServerText.Format(TranslationKeys.ProxyActivityCountryUnblockedOverview, ActivityUser.NameOf(User), CountryName(country)), userId: ActivityUser.Of(User));
        await _proxy.ApplyAsync(CancellationToken.None).ConfigureAwait(false);
        return Done();
    }

    // The country's English name, worked out here rather than taken from the page, so the activity log shows
    // what was really changed.
    private static string CountryName(string code)
    {
        try
        {
            return new System.Globalization.RegionInfo(code).EnglishName;
        }
        catch (ArgumentException)
        {
            return code;
        }
    }

    // An empty JSON body rather than 204: the dashboard asks for JSON, and some browsers fail to read an empty
    // response as JSON, which would show a successful action as failed.
    private OkObjectResult Done() => Ok(new { });

    private BadRequestObjectResult Refuse(string message) => BadRequest(new { Errors = new List<string> { message } });

    /// <summary>
    /// Deletes all People history.
    /// </summary>
    /// <returns>An empty answer.</returns>
    [HttpDelete("Visitors/History")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult DeleteAll()
    {
        _access.ClearBuffered();
        _blocks.Drain();
        _store.DeleteAll();
        _activity.Log(ServerText.Get(TranslationKeys.ProxyActivityHistoryCleared), "Necto.Proxy.HistoryCleared", ServerText.Format(TranslationKeys.ProxyActivityHistoryClearedOverview, ActivityUser.NameOf(User)), userId: ActivityUser.Of(User));
        return Done();
    }
}
