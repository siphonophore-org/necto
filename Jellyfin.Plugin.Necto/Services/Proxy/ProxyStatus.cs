using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Hosting;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Services.Proxy.Reachability;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
using Jellyfin.Plugin.Necto.Api.Ddns;
using Jellyfin.Plugin.Necto.Api.Proxy;

namespace Jellyfin.Plugin.Necto.Services.Proxy;

/// <summary>
/// What the dashboard shows about the proxy.
/// </summary>
public sealed class ProxyStatus
{
    /// <summary>Gets or sets a value indicating whether the proxy is enabled in settings.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets a value indicating whether the proxy is listening.</summary>
    public bool Running { get; set; }

    /// <summary>Gets or sets when the proxy last started (UTC).</summary>
    public DateTime? StartedUtc { get; set; }

    /// <summary>Gets or sets the addresses the proxy listens on.</summary>
    public List<string> Addresses { get; set; } = new();

    /// <summary>Gets or sets the last start error, empty when none.</summary>
    public string Error { get; set; } = string.Empty;

    /// <summary>Gets or sets Jellyfin's address as the proxy reaches it.</summary>
    public string Backend { get; set; } = string.Empty;

    /// <summary>Gets or sets Jellyfin's current HTTP port.</summary>
    public int JellyfinHttpPort { get; set; }

    /// <summary>Gets or sets Jellyfin's current HTTPS port, or zero when Jellyfin does not serve HTTPS.</summary>
    public int JellyfinHttpsPort { get; set; }

    /// <summary>Gets or sets a value indicating whether Jellyfin honours the proxy's forwarding headers.</summary>
    public bool JellyfinTrustsProxy { get; set; }

    /// <summary>Gets or sets a value indicating whether the recovery flag file is present.</summary>
    public bool DisableFlagPresent { get; set; }

    /// <summary>Gets or sets the recovery flag file path, for the instructions on the page.</summary>
    public string DisableFlagPath { get; set; } = string.Empty;

    /// <summary>Gets or sets each route's certificate and name, in the order the routes are listed.</summary>
    public List<RouteStatus> Routes { get; set; } = new();

    /// <summary>Gets or sets the port takeover state (without the snapshot).</summary>
    public TakeoverPhase TakeoverPhase { get; set; }

    /// <summary>Gets or sets when the takeover was applied (UTC).</summary>
    public DateTime? TakeoverAppliedUtc { get; set; }

    /// <summary>Gets or sets why Jellyfin could not be reached through the proxy at the last check, empty when it could.</summary>
    public string TakeoverError { get; set; } = string.Empty;

    /// <summary>Gets or sets the LAN address detected for auto-discovery.</summary>
    public string DetectedLanAddress { get; set; } = string.Empty;

    /// <summary>Gets or sets the proxy's plain HTTP port.</summary>
    public int HttpPort { get; set; }

    /// <summary>Gets or sets the proxy's HTTPS port, the one the router forwards port 443 to.</summary>
    public int HttpsPort { get; set; }

    /// <summary>Gets or sets the LAN address set by the administrator, empty when detected.</summary>
    public string LanAddress { get; set; } = string.Empty;

    /// <summary>Gets or sets the address people use from the internet, empty until a hostname is set.</summary>
    public string PublicUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the internet connections country rules refused since startup, by country code.</summary>
    public Dictionary<string, int> CountryRefused { get; set; } = new();

    /// <summary>Gets or sets a value indicating whether the location database country rules use is loaded.</summary>
    public bool CountryDatabaseLoaded { get; set; }

    /// <summary>Gets or sets the country this server is in, empty while unknown.</summary>
    public string ServerCountry { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a public address of this machine where Jellyfin's own ports answer without the proxy, while
    /// Jellyfin accepts remote clients and listens on every interface; empty when there is none.
    /// </summary>
    public string JellyfinExposedAt { get; set; } = string.Empty;

    /// <summary>Gets or sets who the country rule lets in from the internet, in words; empty when it is off.</summary>
    public string CountryRule { get; set; } = string.Empty;

    /// <summary>Gets or sets why the plugin cannot use its own folders, with the fix; empty when it can.</summary>
    public string FolderProblem { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether every visitor in the last hour looked local, as when something in
    /// front of the proxy hides their addresses.
    /// </summary>
    public bool EveryoneLooksLocal { get; set; }

    /// <summary>Gets or sets whether the first internet route's name points at this server.</summary>
    public NameStatus Name { get; set; } = new();

    /// <summary>Gets or sets the last check from outside the network, empty before any.</summary>
    public ReachabilityResult Reachability { get; set; } = new();
}
