namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// What the dashboard is told after a format.
/// </summary>
/// <param name="Formatted">Whether the disk was formatted and mounted.</param>
/// <param name="MountPoint">Where it was mounted.</param>
/// <param name="ErrorKey">The string key of why it was refused, such as ErrorFormatName, when the plugin refused it.</param>
/// <param name="Error">What the OS said, when the OS failed it.</param>
public sealed record FormatOutcome(bool Formatted, string? MountPoint = null, string? ErrorKey = null, string? Error = null);
