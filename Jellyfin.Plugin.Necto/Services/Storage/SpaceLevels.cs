using System;
using Jellyfin.Plugin.Necto.Configuration.Storage;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// Decides how full a disk is against the Warning and Error stops. Pure, so the rules are testable.
/// </summary>
public static class SpaceLevels
{
    /// <summary>
    /// How far, in points of the disk's share in use, a disk must fall below a stop it passed before it clears.
    /// Without it, a disk sitting right at a stop would pass and clear it on every check.
    /// </summary>
    public const double ClearMargin = 2;

    private const double BytesPerGigabyte = 1024d * 1024 * 1024;

    /// <summary>
    /// Gets the share in use at which a stop is reached on a disk of a given size.
    /// </summary>
    /// <param name="stop">The stop.</param>
    /// <param name="totalBytes">The disk's size.</param>
    /// <returns>The share from 0 to 100, or <c>null</c> when the stop is off or the disk has no size.</returns>
    public static double? Threshold(SpaceStop stop, long totalBytes)
    {
        ArgumentNullException.ThrowIfNull(stop);
        if (!stop.Enabled || totalBytes <= 0)
        {
            return null;
        }

        var percent = stop.Unit == SpaceUnit.Percent
            ? stop.Value
            : 100 - (stop.Value * BytesPerGigabyte * 100 / totalBytes);
        return Math.Clamp(percent, 0, 100);
    }

    /// <summary>
    /// Decides a disk's level from its space, the stops, and its level at the last check.
    /// </summary>
    /// <param name="totalBytes">The disk's size.</param>
    /// <param name="freeBytes">The disk's free space.</param>
    /// <param name="warning">The Warning stop.</param>
    /// <param name="error">The Error stop.</param>
    /// <param name="previous">The level at the last check.</param>
    /// <returns>The level now.</returns>
    public static SpaceLevel Evaluate(long totalBytes, long freeBytes, SpaceStop warning, SpaceStop error, SpaceLevel previous)
    {
        if (totalBytes <= 0)
        {
            return SpaceLevel.None;
        }

        var used = (totalBytes - freeBytes) * 100d / totalBytes;
        if (Reached(used, Threshold(error, totalBytes), previous >= SpaceLevel.Error))
        {
            return SpaceLevel.Error;
        }

        return Reached(used, Threshold(warning, totalBytes), previous >= SpaceLevel.Warning) ? SpaceLevel.Warning : SpaceLevel.None;
    }

    // A stop already passed stays passed until the disk falls a margin below it.
    private static bool Reached(double used, double? threshold, bool wasReached)
        => threshold is { } at && used >= (wasReached ? at - ClearMargin : at);
}
