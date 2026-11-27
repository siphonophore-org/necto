using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Api.Ddns;
namespace Jellyfin.Plugin.Necto.Services.Proxy.Visitors;

/// <summary>
/// What refused a connection.
/// </summary>
public enum BlockReason
{
    /// <summary>The address is on the block list.</summary>
    Address,

    /// <summary>The country rules refused it.</summary>
    Country,

    /// <summary>It is banned for too many failed sign-ins.</summary>
    Ban
}
