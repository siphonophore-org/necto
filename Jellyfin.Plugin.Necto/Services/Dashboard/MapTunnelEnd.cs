namespace Jellyfin.Plugin.Necto.Services.Dashboard;

/// <summary>
/// One end of a WireGuard tunnel, placed by the location database for the Dashboard.
/// </summary>
public sealed class MapTunnelEnd
{
    /// <summary>Gets or sets what it is: Peer, Connection, or Host.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Gets or sets its name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets its state, as the WireGuard tool reports it.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>Gets or sets why its tunnel failed, or null.</summary>
    public string? Error { get; set; }

    /// <summary>Gets or sets a value indicating whether it is connected.</summary>
    public bool Connected { get; set; }

    /// <summary>Gets or sets the two-letter code of the country it connects from or to, or empty when it is not known.</summary>
    public string CountryCode { get; set; } = string.Empty;

    /// <summary>Gets or sets that country's name, or empty when it is not known.</summary>
    public string Country { get; set; } = string.Empty;
}
