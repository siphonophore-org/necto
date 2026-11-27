using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
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
/// Watches the library for requested titles arriving. A new item often has no provider ids until its
/// metadata refresh finishes, so updates are watched as well as additions, and episodes and seasons count
/// toward their show. A scan touches a show once per episode, so changes are gathered per movie or show and
/// checked once things settle. A movie, show or episode leaving the library is checked the same way, once things
/// settle, so a title moved or re-scanned (gone and back under a new id) keeps its requests as they are. The sweep task
/// catches anything added or removed while the server was off.
/// </summary>
[PluginService(ServiceLifetime.Singleton, ServiceType = typeof(IHostedService))]
public sealed class FulfillmentWatcher : IHostedService, IDisposable
{
    private static readonly TimeSpan _settle = TimeSpan.FromSeconds(15);

    // How often what has settled is checked. One loop does it all, so a scan that touches thousands of titles doesn't
    // start a timer for each.
    private static readonly TimeSpan _tick = TimeSpan.FromSeconds(5);

    // Movies and shows changed, the latest of each, and when each first changed.
    private readonly ConcurrentDictionary<Guid, Waiting> _pending = new();

    // Movies and shows that lost an item (themselves, or an episode or season), and when, waiting to be checked.
    private readonly ConcurrentDictionary<Guid, DateTime> _removed = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly ILibraryManager _libraryManager;
    private readonly RequestService _requests;
    private readonly ILogger<FulfillmentWatcher> _logger;
    private Task? _checking;

    /// <summary>
    /// Initializes a new instance of the <see cref="FulfillmentWatcher"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="requests">The request service.</param>
    /// <param name="logger">The logger.</param>
    public FulfillmentWatcher(ILibraryManager libraryManager, RequestService requests, ILogger<FulfillmentWatcher> logger)
    {
        _libraryManager = libraryManager;
        _requests = requests;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded += OnItemChanged;
        _libraryManager.ItemUpdated += OnItemChanged;
        _libraryManager.ItemRemoved += OnItemRemoved;
        _checking = CheckLoopAsync(_stopping.Token);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnItemChanged;
        _libraryManager.ItemUpdated -= OnItemChanged;
        _libraryManager.ItemRemoved -= OnItemRemoved;
        await _stopping.CancelAsync().ConfigureAwait(false);
        if (_checking is { } checking)
        {
            await checking.ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _stopping.Dispose();

    private void OnItemChanged(object? sender, ItemChangeEventArgs e)
    {
        if (e?.Item is null || !RequestService.Enabled || LibraryMatcher.Requestable(e.Item) is not { } item || _stopping.IsCancellationRequested)
        {
            return;
        }

        // The first change starts the wait; later ones for the same title within it ride along.
        // The latest of a burst is checked: a new item's later updates carry the provider ids its first didn't.
        // One whose check has just taken it starts a wait of its own.
        var now = DateTime.UtcNow;
        _pending.AddOrUpdate(item.Id, _ => new Waiting(item, now), (_, waiting) => waiting with { Item = item });
    }

    private void OnItemRemoved(object? sender, ItemChangeEventArgs e)
    {
        // The movie or show whose requests may have lost what fulfilled them.
        var id = e?.Item switch
        {
            Movie or Series => e.Item.Id,
            Episode episode => episode.SeriesId,
            Season season => season.SeriesId,
            _ => Guid.Empty
        };
        if (id != Guid.Empty && !_stopping.IsCancellationRequested && RequestService.Enabled)
        {
            _removed.TryAdd(id, DateTime.UtcNow);
        }
    }

    private async Task CheckLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_tick);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                CheckSettled(DateTime.UtcNow - _settle, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    // Checks every title that has waited out the settle time since it first changed or lost something.
    private void CheckSettled(DateTime settledBefore, CancellationToken cancellationToken)
    {
        foreach (var (id, entry) in _pending)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (entry.FirstUtc > settledBefore || !_pending.TryRemove(id, out var waiting))
            {
                continue;
            }

            try
            {
                // Fulfil only changes a request that is still open, so the kept open requests can't undo anything, and
                // any write to a request means they're read again for the next title.
                _requests.Fulfill(waiting.Item, _requests.OpenRequests());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Checking {Item} against requests failed", waiting.Item.Name);
            }
        }

        // Every title that lost something in this round is checked at once, reading only the requests they fulfilled.
        var removed = new List<Guid>();
        foreach (var (id, since) in _removed)
        {
            if (since <= settledBefore && _removed.TryRemove(id, out _))
            {
                removed.Add(id);
            }
        }

        if (removed.Count == 0 || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            _requests.Recheck(removed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Checking requests after a library item was removed failed");
        }
    }

    // A title waiting to be checked: its latest change, and when it first changed.
    private sealed record Waiting(BaseItem Item, DateTime FirstUtc);
}
