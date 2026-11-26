namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// Why a disk could not be mounted again.
/// </summary>
public enum RemountFailure
{
    /// <summary>The OS reported an error the plugin has no wording for.</summary>
    Other = 0,

    /// <summary>The OS does not let Jellyfin mount disks.</summary>
    Denied = 1,

    /// <summary>The share refused the saved sign-in, or there is none.</summary>
    SignIn = 2,

    /// <summary>The server or the disk can't be reached.</summary>
    Unreachable = 3,

    /// <summary>The mount call worked, but the disk did not show up at its mount point.</summary>
    NotMounted = 4
}
