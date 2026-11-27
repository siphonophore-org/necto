namespace Jellyfin.Plugin.Necto.Configuration.Storage;

/// <summary>
/// Where a share is mounted from, set by hand for a share the plugin never saw mounted.
/// </summary>
public sealed class ShareAddress
{
    /// <summary>Gets or sets the mount point of the share.</summary>
    public string MountPoint { get; set; } = string.Empty;

    /// <summary>Gets or sets the address, such as smb://nas/Media on macOS or \\nas\Media on Windows.</summary>
    public string Address { get; set; } = string.Empty;
}
