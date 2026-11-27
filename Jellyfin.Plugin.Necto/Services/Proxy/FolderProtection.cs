using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Services.Ddns;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Proxy;

/// <summary>
/// Keeps Necto's key ring and the proxy's folder, which holds certificate keys, the Let's Encrypt account and the
/// visitor history, to the account Jellyfin runs as, as Jellyfin starts. A folder this account cannot open is
/// reported on the Proxy page with the fix, rather than locked further.
/// </summary>
public sealed class FolderProtection : IHostedService
{
    private readonly IApplicationPaths _paths;
    private readonly ProxyPaths _proxyPaths;
    private readonly ILogger<FolderProtection> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FolderProtection"/> class.
    /// </summary>
    /// <param name="paths">Gives the key ring folder.</param>
    /// <param name="proxyPaths">Gives the proxy's folder.</param>
    /// <param name="logger">The logger.</param>
    public FolderProtection(IApplicationPaths paths, ProxyPaths proxyPaths, ILogger<FolderProtection> logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(proxyPaths);
        _paths = paths;
        _proxyPaths = proxyPaths;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        Protect();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Checks the folders can be opened, wraps the key ring's keys where the system can, and narrows each folder to
    /// Jellyfin's account. A failure is logged, not thrown, so Necto still works, only less privately.
    /// </summary>
    public void Protect()
    {
        var folders = new[] { StableSecretProtection.KeyDirectory(_paths, DdnsServices.SecretsName), _proxyPaths.DataDirectory };

        // Checked before narrowing: a folder this account cannot open is one it cannot narrow either, and the
        // check is what tells the administrator how to give it back.
        var problem = FolderAccess.Check(folders);
        if (problem.Length > 0)
        {
            _logger.LogError("{Problem}", problem);
            return;
        }

        KeyRingProtection.Protect(folders[0], _logger);
        foreach (var folder in folders)
        {
            try
            {
                SecretFile.ProtectFolder(folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException or InvalidOperationException or System.Security.SecurityException)
            {
                _logger.LogWarning(ex, "Could not restrict access to {Folder}", folder);
            }
        }
    }
}
