using System;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Requests;

/// <summary>
/// Holds the protector the Requests keys are stored with, made once from the server's paths.
/// </summary>
[PluginService(ServiceLifetime.Singleton)]
public sealed class RequestSecrets
{
    /// <summary>The stable name the keys are encrypted under. Changing it makes every stored key unreadable.</summary>
    internal const string SecretsName = "Jellyfin.Plugin.Necto.Requests";

    private static SecretProtector? _current;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestSecrets"/> class.
    /// </summary>
    /// <param name="paths">The server's paths, where the key ring lives.</param>
    /// <param name="logger">Warns when keys cannot be encrypted.</param>
    public RequestSecrets(IApplicationPaths paths, ILogger<RequestSecrets> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        Protector = StableSecretProtection.CreateProtector(paths, SecretsName, logger);
        _current = Protector;
    }

    /// <summary>Gets the protector the keys are stored with, or one with no key ring before the services start.</summary>
    public static SecretProtector Current => _current ??= StableSecretProtection.CreateProtector(null, SecretsName, NullLogger.Instance);

    /// <summary>Gets this instance's protector.</summary>
    public SecretProtector Protector { get; }
}
