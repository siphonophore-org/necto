using System;
using System.IO;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Invites;

/// <summary>
/// Keeps each invite's use count, wrong PIN count, and recent signups in a file beside the configuration. Every change
/// is one atomic update, so a signup and an action in the Invites tool never overwrite each other.
/// </summary>
[PluginService(ServiceLifetime.Singleton)]
public sealed class InviteStatusStore
{
    /// <summary>The file the counters are kept in, in the plugin configurations folder.</summary>
    public const string FileName = "Jellyfin.Plugin.Necto.InviteStatus.json";

    private readonly JsonFileStore<InviteStatusData> _file;

    /// <summary>
    /// Initializes a new instance of the <see cref="InviteStatusStore"/> class.
    /// </summary>
    /// <param name="paths">Gives the folder the counters are kept in.</param>
    /// <param name="logger">Writes the server log.</param>
    public InviteStatusStore(IApplicationPaths paths, ILogger<InviteStatusStore> logger)
        : this(Path.Join((paths ?? throw new ArgumentNullException(nameof(paths))).PluginConfigurationsPath, FileName), logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InviteStatusStore"/> class with its file at a path.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="logger">Writes the server log.</param>
    public InviteStatusStore(string path, ILogger? logger)
    {
        _file = new JsonFileStore<InviteStatusData>(path, logger);
    }

    /// <summary>
    /// Reads every invite's counters.
    /// </summary>
    /// <returns>A copy of the counters.</returns>
    public InviteStatusData Load() => _file.Load();

    /// <summary>
    /// Reads one invite's counters.
    /// </summary>
    /// <param name="id">The invite.</param>
    /// <returns>The counters, or <c>null</c> when the invite was never used.</returns>
    public InviteStatus? Find(Guid id) => _file.Load().Invites.TryGetValue(id, out var status) ? status : null;

    /// <summary>
    /// Changes the counters in one atomic update.
    /// </summary>
    /// <param name="mutate">Changes the counters, and returns <c>true</c> to save them.</param>
    /// <returns><c>true</c> when they were saved.</returns>
    public bool Update(Func<InviteStatusData, bool> mutate) => _file.UpdateIf(mutate);
}
