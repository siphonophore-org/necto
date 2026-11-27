using System;
using System.IO;
using System.Linq;
using MediaBrowser.Common.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Services.Proxy.Reachability;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
using Jellyfin.Plugin.Necto.Api.Ddns;

namespace Jellyfin.Plugin.Necto.Services.Proxy;

/// <summary>
/// Where the proxy keeps its files, beside the plugin's configuration.
/// </summary>
public sealed class ProxyPaths
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProxyPaths"/> class.
    /// </summary>
    /// <param name="paths">The application paths.</param>
    public ProxyPaths(IApplicationPaths paths)
        : this(Root(paths))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ProxyPaths"/> class rooted at a directory.
    /// </summary>
    /// <param name="dataDirectory">The directory.</param>
    internal ProxyPaths(string dataDirectory)
    {
        DataDirectory = dataDirectory;
    }

    // The folder under the plugin configurations folder.
    private static string Root(IApplicationPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return Path.Join(paths.PluginConfigurationsPath, "Jellyfin.Plugin.Necto.Proxy");
    }

    /// <summary>Gets the proxy's data directory.</summary>
    public string DataDirectory { get; }

    /// <summary>Gets the folder that holds each route's certificate files.</summary>
    public string RoutesDirectory => Path.Join(DataDirectory, "routes");

    /// <summary>
    /// Gives the certificate files of one route.
    /// </summary>
    /// <param name="routeId">The route's id, which is only letters and digits.</param>
    /// <returns>The route's files.</returns>
    /// <exception cref="ArgumentException">The id holds anything else, which could reach outside the folder.</exception>
    public CertificatePaths ForRoute(string routeId)
    {
        if (string.IsNullOrEmpty(routeId) || routeId.Length > 64 || !routeId.All(char.IsAsciiLetterOrDigit))
        {
            throw new ArgumentException("A route id is letters and digits only.", nameof(routeId));
        }

        return new CertificatePaths(Path.Join(RoutesDirectory, routeId.ToLowerInvariant()));
    }

    /// <summary>Gets the orchestrator state file.</summary>
    public string StateFile => Path.Join(DataDirectory, "network-state.json");

    /// <summary>Gets the directory holding the Let's Encrypt account key, the issued certificate and its state.</summary>
    public string AcmeDirectory => Path.Join(DataDirectory, "acme");

    /// <summary>Gets the Let's Encrypt account key, as PEM.</summary>
    public string AcmeAccountKeyFile => Path.Join(AcmeDirectory, "account.pem");

    /// <summary>Gets the root the self-signed certificate is issued from, with its private key.</summary>
    public string SelfSignedRootFile => Path.Join(DataDirectory, "selfsigned-root.pfx");

    /// <summary>Gets the file listing DNS-01 TXT records still to be removed.</summary>
    public string AcmeTxtCleanupFile => Path.Join(AcmeDirectory, "txt-cleanup.json");

    /// <summary>
    /// Gets the recovery flag. When this file exists at startup, Jellyfin's network settings are restored and
    /// the proxy stays off. Creating it is the documented way out of a broken setup without the dashboard.
    /// </summary>
    public string DisableFlag => Path.Join(DataDirectory, "DISABLE");
}
