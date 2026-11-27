using System;
using System.IO;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.Necto.Services.Proxy;

/// <summary>
/// Puts Jellyfin's network settings back when Necto is uninstalled. Uninstalling runs from the plugin class, outside
/// the service container, so the configuration manager is kept here once the orchestrator is built.
/// </summary>
public static class ProxyUninstall
{
    private static IConfigurationManager? _configuration;
    private static ILogger _logger = NullLogger.Instance;

    /// <summary>
    /// Keeps the configuration manager the restore writes through.
    /// </summary>
    /// <param name="configuration">Jellyfin's configuration manager.</param>
    /// <param name="logger">The logger for the restore.</param>
    public static void Use(IConfigurationManager configuration, ILogger logger)
    {
        _configuration = configuration;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Restores Jellyfin's network settings if the proxy had Jellyfin trust it. Never throws.
    /// </summary>
    /// <param name="paths">Jellyfin's application paths, which locate the proxy's state.</param>
    public static void Restore(IApplicationPaths paths)
    {
        if (_configuration is null || paths is null)
        {
            return;
        }

        try
        {
            JellyfinNetworkOrchestrator.RestoreForUninstall(new ProxyPaths(paths), _configuration, _logger);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogError(ex, "Could not restore Jellyfin's network settings during uninstall");
        }
    }
}
