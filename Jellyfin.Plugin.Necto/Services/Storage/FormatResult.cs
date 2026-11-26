namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// What came of formatting a disk.
/// </summary>
/// <param name="Formatted">Whether the disk was formatted.</param>
/// <param name="MountPoint">Where it was mounted, when the formatter knows, else <c>null</c> and the new mount is found.</param>
/// <param name="Error">What the OS said went wrong, when it did.</param>
public sealed record FormatResult(bool Formatted, string? MountPoint = null, string? Error = null)
{
    /// <summary>
    /// The result of a format that failed.
    /// </summary>
    /// <param name="error">What the OS said.</param>
    /// <returns>The result.</returns>
    public static FormatResult Failed(string error) => new(false, null, error);
}
