namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// Whether a disk is inside the server, plugged into it, or reached over the network.
/// </summary>
public enum DiskKind
{
    /// <summary>A disk inside the server.</summary>
    Local = 0,

    /// <summary>A network share, such as NFS or SMB.</summary>
    Share = 1,

    /// <summary>A disk plugged in from outside, such as over USB or Thunderbolt.</summary>
    External = 2
}
