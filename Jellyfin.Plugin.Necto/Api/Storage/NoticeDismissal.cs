namespace Jellyfin.Plugin.Necto.Api.Storage;

/// <summary>
/// A disk notice to dismiss.
/// </summary>
public sealed class NoticeDismissal
{
    /// <summary>Gets or sets the mount point of the disk the notice is about.</summary>
    public string MountPoint { get; set; } = string.Empty;

    /// <summary>Gets or sets the notice's key, which changes when what the notice says changes.</summary>
    public string Key { get; set; } = string.Empty;
}
