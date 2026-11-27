using System;
using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Configuration;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// Jellyfin's own folders, as its dashboard lists them: program data, web, logs, image cache, metadata, cache, and
/// transcodes. A disk that holds one is used by Jellyfin, even with no library on it.
/// </summary>
public class JellyfinFolders
{
    private readonly IServerConfigurationManager? _config;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinFolders"/> class.
    /// </summary>
    /// <param name="config">The server configuration, which gives the paths.</param>
    public JellyfinFolders(IServerConfigurationManager config)
    {
        _config = config;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinFolders"/> class with no folders, for tests.
    /// </summary>
    protected JellyfinFolders()
    {
    }

    /// <summary>
    /// Gets Jellyfin's own folders, each with the string key of its name.
    /// </summary>
    /// <returns>The key, such as LabelCache, and the folder.</returns>
    public virtual IReadOnlyList<(string Key, string Path)> Get()
    {
        if (_config is null)
        {
            return Array.Empty<(string, string)>();
        }

        var paths = _config.ApplicationPaths;
        return new List<(string, string)>
        {
            ("LabelProgramData", paths.ProgramDataPath),
            ("LabelWeb", paths.WebPath),
            ("LabelLogs", paths.LogDirectoryPath),
            ("LabelImageCache", paths.ImageCachePath),
            ("LabelMetadata", paths.InternalMetadataPath),
            ("LabelCache", paths.CachePath),
            ("LabelTranscodes", _config.GetTranscodePath())
        };
    }

    /// <summary>
    /// Tells whether Jellyfin only reads a folder, so it is not checked for writing, such as its web files.
    /// </summary>
    /// <param name="key">The folder's key.</param>
    /// <returns><c>true</c> for a folder Jellyfin only reads.</returns>
    public static bool ReadOnly(string key) => key == "LabelWeb";
}
