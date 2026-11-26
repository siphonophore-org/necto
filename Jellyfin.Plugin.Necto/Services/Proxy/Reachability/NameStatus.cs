using System.Collections.Generic;
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
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
using Jellyfin.Plugin.Necto.Api.Ddns;
using Jellyfin.Plugin.Necto.Api.Proxy;

namespace Jellyfin.Plugin.Necto.Services.Proxy.Reachability;

/// <summary>
/// Whether the public name people use points at this server.
/// </summary>
public sealed class NameStatus
{
    /// <summary>Gets or sets the name checked: the first exact public hostname, empty when there is none.</summary>
    public string Hostname { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the name resolves to this server's public address.</summary>
    public bool Resolves { get; set; }

    /// <summary>Gets or sets the addresses the name resolved to.</summary>
    public List<string> Addresses { get; set; } = new();

    /// <summary>Gets or sets this server's public address the name was compared with, empty while unknown.</summary>
    public string PublicAddress { get; set; } = string.Empty;

    /// <summary>Gets or sets what went wrong, empty when nothing did.</summary>
    public string Error { get; set; } = string.Empty;

    /// <summary>
    /// Copies the status, so a caller never holds the cached one.
    /// </summary>
    /// <returns>The copy.</returns>
    public NameStatus Copy()
    {
        var copy = (NameStatus)MemberwiseClone();
        copy.Addresses = new List<string>(Addresses);
        return copy;
    }
}
