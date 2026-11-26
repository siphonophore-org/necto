using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Hosting;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Services.Proxy.Reachability;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
using Jellyfin.Plugin.Necto.Api.Ddns;
using Jellyfin.Plugin.Necto.Api.Proxy;
namespace Jellyfin.Plugin.Necto.Services.Ddns;

/// <summary>
/// The public IP addresses discovered for the server, plus a note for any family that was wanted but
/// could not be resolved to a usable public address.
/// </summary>
public sealed class DetectedIP
{
    /// <summary>Gets the detected public IPv4 address, or <c>null</c> when none was found.</summary>
    public string? IPv4 { get; init; }

    /// <summary>Gets the detected public IPv6 address, or <c>null</c> when none was found.</summary>
    public string? IPv6 { get; init; }

    /// <summary>
    /// Gets a human readable note explaining why IPv4 detection produced no address, or <c>null</c> when
    /// IPv4 was found or was not requested. Set when a detected address looked internal.
    /// </summary>
    public string? IPv4Note { get; init; }

    /// <summary>
    /// Gets a human readable note explaining why IPv6 detection produced no address, or <c>null</c> when
    /// IPv6 was found or was not requested. Set when a detected address looked internal.
    /// </summary>
    public string? IPv6Note { get; init; }
}
