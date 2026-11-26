using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Services.Issues.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Necto.Services.Issues;

/// <summary>
/// Watches the library for items with open issues being rescanned, replaced or removed, and marks their issues
/// "File changed since the report" for an administrator to look at. A scan touches an item several times, so changes
/// are gathered and checked every few seconds. The cleanup task catches anything changed while the server was off.
/// </summary>
[PluginService(ServiceLifetime.Singleton, ServiceType = typeof(IHostedService))]
public sealed class IssueFileWatcher : IHostedService, IDisposable
{
    private static readonly TimeSpan _tick = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<Guid, byte> _changed = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly ILibraryManager _libraryManager;
    private readonly IssueService _issues;
    private readonly ILogger<IssueFileWatcher> _logger;
    private Task? _checking;

    /// <summary>
    /// Initializes a new instance of the <see cref="IssueFileWatcher"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="issues">The issue service.</param>
    /// <param name="logger">The logger.</param>
    public IssueFileWatcher(ILibraryManager libraryManager, IssueService issues, ILogger<IssueFileWatcher> logger)
    {
        _libraryManager = libraryManager;
        _issues = issues;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemUpdated += OnItemChanged;
        _libraryManager.ItemRemoved += OnItemChanged;
        _checking = CheckLoopAsync(_stopping.Token);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemUpdated -= OnItemChanged;
        _libraryManager.ItemRemoved -= OnItemChanged;
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
        if (e?.Item is { IsFolder: false } item && IssueService.Enabled && !_stopping.IsCancellationRequested)
        {
            _changed[item.Id] = 0;
        }
    }

    // Checks what changed since the last look, until the server stops.
    private async Task CheckLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_tick, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var ids = _changed.Keys.ToList();
            foreach (var id in ids)
            {
                _changed.TryRemove(id, out _);
            }

            if (ids.Count == 0)
            {
                continue;
            }

            try
            {
                _issues.CheckFiles(ids);
            }
            catch (Exception ex)
            {
                // Checked again at the next change or by the cleanup task.
                _logger.LogWarning(ex, "Checking issues for changed files failed");
            }
        }
    }
}
