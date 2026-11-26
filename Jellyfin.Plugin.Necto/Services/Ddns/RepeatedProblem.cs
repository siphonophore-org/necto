using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Hosting;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Services.Proxy.Reachability;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
using Jellyfin.Plugin.Necto.Api.Ddns;
using Jellyfin.Plugin.Necto.Api.Proxy;

namespace Jellyfin.Plugin.Necto.Services.Ddns;

/// <summary>
/// Keeps a problem that repeats, such as a database that cannot be written or detection that keeps failing,
/// from filling the log: the first time is a warning, repeats are debug lines, and the first success after
/// it says so at information.
/// </summary>
internal sealed class RepeatedProblem
{
    private int _failing;

    /// <summary>
    /// Reports the problem happening again.
    /// </summary>
    /// <param name="log">Writes the line at the level given: warning the first time, debug after.</param>
    public void Failed(Action<LogLevel> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        log(Interlocked.Exchange(ref _failing, 1) == 0 ? LogLevel.Warning : LogLevel.Debug);
    }

    /// <summary>
    /// Reports success, which ends the problem.
    /// </summary>
    /// <param name="recovered">Writes the recovery line, only when the problem was current.</param>
    public void Succeeded(Action? recovered = null)
    {
        if (Interlocked.Exchange(ref _failing, 0) == 1)
        {
            recovered?.Invoke();
        }
    }
}
