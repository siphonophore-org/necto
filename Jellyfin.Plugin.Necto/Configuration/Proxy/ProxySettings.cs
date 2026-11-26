using System.Collections.Generic;

namespace Jellyfin.Plugin.Necto.Configuration.Proxy;

/// <summary>
/// The Proxy tool's routes and its settings. Nothing listens until <see cref="Enabled"/> is set.
/// </summary>
public class ProxySettings
{
    /// <summary>Gets or sets a value indicating whether the proxy runs.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the routes, each a host name forwarded to one service. Only the Proxy routes change them.</summary>
    public List<ProxyRoute> Routes { get; set; } = new();

    /// <summary>Gets or sets a value indicating whether the administrator accepted the Let's Encrypt terms. Nothing is asked of Let's Encrypt until then.</summary>
    public bool AcceptTerms { get; set; }

    /// <summary>
    /// Gets or sets the listener and protection values the administrator changed. Anything left empty follows the
    /// built-in default.
    /// </summary>
    public ProxyAdvancedSettings Advanced { get; set; } = new();

    /// <summary>Gets or sets the visitor history settings, which the Dashboard map reads.</summary>
    public VisitorSettings Visitors { get; set; } = new();
}
