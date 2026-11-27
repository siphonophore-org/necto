namespace Jellyfin.Plugin.Necto.Services.Dashboard;

/// <summary>
/// What the Dashboard map counts in one country, one count per layer.
/// </summary>
public sealed class MapCountry
{
    /// <summary>Gets or sets the two-letter country code.</summary>
    public string CountryCode { get; set; } = string.Empty;

    /// <summary>Gets or sets the country's name, as the location database writes it, or empty when it is not known.</summary>
    public string Country { get; set; } = string.Empty;

    /// <summary>Gets or sets the people the proxy saw from the country.</summary>
    public int Visitors { get; set; }

    /// <summary>Gets or sets the failed sign-ins from the country.</summary>
    public int Failed { get; set; }

    /// <summary>Gets or sets the connections the proxy refused from the country.</summary>
    public int Blocked { get; set; }

    /// <summary>Gets or sets the WireGuard peers that connect from the country.</summary>
    public int Peers { get; set; }

    /// <summary>Gets or sets the WireGuard connections to hosts in the country.</summary>
    public int Connections { get; set; }

    /// <summary>Gets or sets a value indicating whether this server is in the country.</summary>
    public bool Server { get; set; }
}
