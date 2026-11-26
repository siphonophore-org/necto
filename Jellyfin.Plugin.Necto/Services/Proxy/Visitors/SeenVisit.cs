using System;
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
using Jellyfin.Plugin.Necto.Api.Ddns;
using Jellyfin.Plugin.Necto.Api.Proxy;

namespace Jellyfin.Plugin.Necto.Services.Proxy.Visitors;

/// <summary>
/// An internet address that reached Jellyfin through the proxy, whether or not it went on to sign in.
/// </summary>
public sealed class SeenVisit
{
    /// <summary>Gets or sets the address.</summary>
    public string Ip { get; set; } = string.Empty;

    /// <summary>Gets or sets the address it used, like <c>https://media.example.com:8920</c>.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Gets or sets a short description of its browser or app.</summary>
    public string Device { get; set; } = string.Empty;

    /// <summary>Gets or sets the two-letter country code, empty when unknown.</summary>
    public string CountryCode { get; set; } = string.Empty;

    /// <summary>Gets or sets the country name, empty when unknown.</summary>
    public string Country { get; set; } = string.Empty;

    /// <summary>Gets or sets how many requests it made.</summary>
    public int Requests { get; set; }

    /// <summary>Gets or sets the first request (UTC).</summary>
    public DateTime FirstUtc { get; set; }

    /// <summary>Gets or sets the latest request (UTC).</summary>
    public DateTime LastUtc { get; set; }
}
