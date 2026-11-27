using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Necto.Services.Requests;

/// <summary>
/// Keeps the request page's home rows ready. On start it reads the stored rows, and Radarr's and Sonarr's libraries as
/// last read, from the cache folder. A few seconds later it reads the library into memory and, while the stored rows
/// are current, the rows once as an administrator would see them, so the first visit doesn't pay for a first read.
/// About 30 seconds after start it stores the rows and libraries afresh when they're missing, over a day old or (the
/// rows) in another language; the daily task stores them afresh after that. Then, while the page is in
/// use (within SearchService.InUseFor of its last use), every eight minutes it reads Radarr's and Sonarr's whole
/// libraries and the rows (from the stored copy while it's current), before what was read last time goes stale. So
/// opening the page never waits on TMDb or on Radarr and Sonarr. Nobody using the page, their libraries are read only
/// by the daily task; the first use after that is marked from the copy kept, and has them read again straight away,
/// without waiting for it.
/// </summary>
[PluginService(ServiceLifetime.Singleton, ServiceType = typeof(IHostedService))]
public sealed class DiscoveryWarmer : IHostedService, IDisposable
{
    // Once the server answers, ahead of most first visits. The rows are read only from what's kept, so this asks
    // nothing of TMDb, Radarr or Sonarr that a visit wouldn't.
    private static readonly TimeSpan _primeAfter = TimeSpan.FromSeconds(5);

    // After the server has started, so warming doesn't compete with startup.
    private static readonly TimeSpan _firstAfter = TimeSpan.FromSeconds(30);

    // Inside how long Radarr and Sonarr's answers are kept (ArrClient.OwnedFor), so they're renewed before they lapse.
    private static readonly TimeSpan _every = TimeSpan.FromMinutes(8);

    private readonly SearchService _search;
    private readonly TmdbDiscovery _tmdb;
    private readonly ArrClient _arr;
    private readonly ILogger<DiscoveryWarmer> _logger;
    private readonly LibraryMatcher? _library;
    private readonly RequestService? _requests;
    private readonly RequestItems? _items;
    private readonly IUserManager? _users;
    private readonly CancellationTokenSource _stopping = new();
    private Task? _loop;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiscoveryWarmer"/> class.
    /// </summary>
    /// <param name="search">The search service, which reads the rows.</param>
    /// <param name="tmdb">TMDb, whose stored rows are read on start.</param>
    /// <param name="arr">Radarr and Sonarr, whose libraries are read each time.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="library">The library matcher, whose index is read ahead of the first visit.</param>
    /// <param name="requests">The request service, for the access of the administrator the rows are first read as.</param>
    /// <param name="items">The request page's items, built once for the rows read first.</param>
    /// <param name="users">Jellyfin's users, for an administrator to read the rows as.</param>
    public DiscoveryWarmer(SearchService search, TmdbDiscovery tmdb, ArrClient arr, ILogger<DiscoveryWarmer> logger, LibraryMatcher? library = null, RequestService? requests = null, RequestItems? items = null, IUserManager? users = null)
    {
        _library = library;
        _requests = requests;
        _items = items;
        _users = users;
        _search = search;
        _tmdb = tmdb;
        _arr = arr;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // The stored rows, at once, so the page has them from the first visit after a restart.
        _tmdb.LoadStore();
        _arr.LoadLibraries();
        _loop = RunAsync(_stopping.Token);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync().ConfigureAwait(false);
        if (_loop is not null)
        {
            await _loop.ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _stopping.Dispose();

    private async Task RunAsync(CancellationToken stopping)
    {
        try
        {
            await Task.Delay(_primeAfter, stopping).ConfigureAwait(false);

            // The library, read into memory ahead of the first visit, which would otherwise match every title it lists
            // through the database while this is read.
            _library?.WarmIndex();
            await PrimeAsync(stopping).ConfigureAwait(false);
            await Task.Delay(_firstAfter - _primeAfter, stopping).ConfigureAwait(false);

            // A copy missing (a fresh install, a cleared cache), more than a day old, or in another language is stored
            // afresh now, rather than waiting for the daily task: TMDb's rows, and Radarr's and Sonarr's libraries.
            if (!_tmdb.StoreIsCurrent || !_arr.LibrariesAreCurrent)
            {
                try
                {
                    // Storing the rows reads Radarr and Sonarr too; with the rows current, only they are read.
                    if (_tmdb.StoreIsCurrent)
                    {
                        await _arr.RefreshLibrariesAsync(stopping).ConfigureAwait(false);
                    }
                    else
                    {
                        await _search.RefreshStoredAsync(stopping).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (!stopping.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Storing the request page's rows failed; the daily task tries again");
                }
            }

            while (true)
            {
                // Taken before looking whether the page is in use, so a use in between still wakes the wait below.
                var woken = _search.Woken;
                if (_search.InUse)
                {
                    try
                    {
                        // Radarr's and Sonarr's whole libraries first, so every list, search and More Like This can say
                        // which titles they have at once; then the rows, which then ask nothing more of them.
                        await _arr.RefreshLibrariesAsync(stopping).ConfigureAwait(false);
                        await _search.WarmAsync(stopping).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (!stopping.IsCancellationRequested)
                    {
                        // Warming is only ahead of time: the page reads the rows itself if this can't.
                        _logger.LogDebug(ex, "Warming the request page's rows failed");
                    }
                }

                // Until the next round, or the page's first use after a quiet spell, whichever comes first.
                await Task.WhenAny(Task.Delay(_every, stopping), woken).ConfigureAwait(false);
                stopping.ThrowIfCancellationRequested();
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping.
        }
    }

    // Reads the home rows once, and builds their items, as the first administrator would see them: the first read
    // after a start costs seconds (the code's first run, the library's and the rows' first look-ups), which would
    // otherwise fall on whoever opens the page first.
    private async Task PrimeAsync(CancellationToken stopping)
    {
        try
        {
            var admin = _users?.GetUsers().FirstOrDefault(u => u.HasPermission(PermissionKind.IsAdministrator) && !u.HasPermission(PermissionKind.IsDisabled));
            // Rows that aren't stored yet would be read from TMDb, which the store at _firstAfter does anyway.
            if (!_tmdb.StoreIsCurrent || admin is null || _requests?.GetAccess(admin.Id) is not { CanRequest: true } viewer)
            {
                return;
            }

            var rows = await _search.PrimeAsync(viewer, stopping).ConfigureAwait(false);
            _items?.For(rows.SelectMany(r => r.Items).ToList(), viewer.User);
        }
        catch (Exception ex) when (!stopping.IsCancellationRequested)
        {
            // Only ahead of time: the first visit reads the rows itself.
            _logger.LogDebug(ex, "Reading the request page's rows ahead of the first visit failed");
        }
    }
}
