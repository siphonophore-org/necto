using System;
using System.Linq;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// Adds a folder to a library the way Jellyfin's own API does, with real-time monitoring paused for a moment so the
/// folder watchers miss the change.
/// </summary>
public sealed class LibraryFolders
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILibraryMonitor _libraryMonitor;
    private readonly ILogger<LibraryFolders> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryFolders"/> class.
    /// </summary>
    /// <param name="libraryManager">Lists the libraries and adds folders to them.</param>
    /// <param name="libraryMonitor">Paused while a folder is added.</param>
    /// <param name="logger">The logger.</param>
    public LibraryFolders(ILibraryManager libraryManager, ILibraryMonitor libraryMonitor, ILogger<LibraryFolders> logger)
    {
        _libraryManager = libraryManager;
        _libraryMonitor = libraryMonitor;
        _logger = logger;
    }

    /// <summary>
    /// Tells whether a library a folder can be added to exists. Collections is a folder Jellyfin keeps for itself,
    /// not a library to put media in.
    /// </summary>
    /// <param name="library">The library's name.</param>
    /// <returns><c>true</c> when it exists and takes media.</returns>
    public bool Exists(string library)
        => _libraryManager.GetVirtualFolders().Any(f => string.Equals(f.Name, library, StringComparison.Ordinal) && f.CollectionType != CollectionTypeOptions.boxsets);

    /// <summary>
    /// Adds a folder to a library, telling rather than throwing when Jellyfin refuses it, such as a folder the library
    /// already has.
    /// </summary>
    /// <param name="library">The library's name.</param>
    /// <param name="path">The folder.</param>
    /// <returns><c>true</c> when it was added.</returns>
    public bool TryAdd(string library, string path)
    {
        try
        {
            Add(library, path);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.IO.IOException)
        {
            _logger.LogWarning(ex, "Could not add {Path} to the {Library} library", path, library);
            return false;
        }
    }

    /// <summary>
    /// Adds a folder to a library.
    /// </summary>
    /// <param name="library">The library's name.</param>
    /// <param name="path">The folder.</param>
    public void Add(string library, string path)
    {
        _libraryMonitor.Stop();
        try
        {
            _libraryManager.AddMediaPath(library, new MediaPathInfo(path));
            _logger.LogInformation("Added {Path} to the {Library} library", path, library);
        }
        finally
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(1000).ConfigureAwait(false);
                _libraryMonitor.Start();
            });
        }
    }
}
