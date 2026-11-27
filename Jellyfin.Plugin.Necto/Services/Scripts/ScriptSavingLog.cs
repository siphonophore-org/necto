using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Necto.Services.Scripts;

/// <summary>
/// Hands a configuration save the server log once the server starts, so a vault or key ring problem while the
/// script secrets are stored reaches the log.
/// </summary>
public sealed class ScriptSavingLog : IHostedService
{
    private readonly ILogger<ScriptSavingLog> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScriptSavingLog"/> class.
    /// </summary>
    /// <param name="logger">The server log a save writes to.</param>
    public ScriptSavingLog(ILogger<ScriptSavingLog> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        ScriptSettingsSaving.Logger = _logger;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
