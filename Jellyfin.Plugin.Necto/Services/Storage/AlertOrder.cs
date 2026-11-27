using System;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.Storage;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// Keeps the Error alert past the Warning alert, so a disk is low on space before it is full, and tidies each disk's
/// own alerts. Pure, so the rules are testable.
/// </summary>
public static class AlertOrder
{
    /// <summary>The temperatures, in °C, the Too hot alert can be set to.</summary>
    public static readonly (int Min, int Max) HotRange = (40, 80);

    /// <summary>The shares of rated life the Worn out alert can be set to.</summary>
    public static readonly (int Min, int Max) WornRange = (50, 100);

    /// <summary>How far past the Warning alert the Error alert sits at least, in points of usage.</summary>
    public const double PercentStep = 1;

    /// <summary>How far past the Warning alert the Error alert sits at least, in GB of free space.</summary>
    public const double GigabyteStep = 10;

    /// <summary>
    /// Orders the global alerts and every disk's own alerts, keeps the health alerts in range, and keeps one override and one share address per disk,
    /// the last one saved.
    /// </summary>
    /// <param name="config">The settings, changed in place.</param>
    public static void Normalize(StorageConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        Normalize(config.WarningStop, config.ErrorStop);
        config.HealthAlerts ??= new HealthAlerts();
        config.HealthAlerts.HotCelsius = Math.Clamp(config.HealthAlerts.HotCelsius, HotRange.Min, HotRange.Max);
        config.HealthAlerts.WornPercent = Math.Clamp(config.HealthAlerts.WornPercent, WornRange.Min, WornRange.Max);
        var comparer = StringComparer.FromComparison(MountPaths.Comparison);
        config.DiskOverrides = (config.DiskOverrides ?? new())
            .Where(o => o is not null && !string.IsNullOrWhiteSpace(o.MountPoint))
            .Reverse()
            .DistinctBy(o => MountPaths.Normalize(o.MountPoint), comparer)
            .Reverse()
            .ToList();
        config.ShareAddresses = (config.ShareAddresses ?? new())
            .Where(a => a is not null && !string.IsNullOrWhiteSpace(a.MountPoint) && !string.IsNullOrWhiteSpace(a.Address))
            .Select(a => new ShareAddress { MountPoint = MountPaths.Normalize(a.MountPoint), Address = a.Address.Trim() })
            .Reverse()
            .DistinctBy(a => a.MountPoint, comparer)
            .Reverse()
            .ToList();
        foreach (var disk in config.DiskOverrides)
        {
            disk.MountPoint = MountPaths.Normalize(disk.MountPoint);
            disk.WarningStop ??= new SpaceStop { Enabled = false };
            disk.ErrorStop ??= new SpaceStop { Enabled = false };
            Normalize(disk.WarningStop, disk.ErrorStop);
        }
    }

    /// <summary>
    /// Moves the Error alert past the Warning alert when both are on and it is not already, measured in the Warning
    /// alert's unit.
    /// </summary>
    /// <param name="warning">The Warning alert, changed in place.</param>
    /// <param name="error">The Error alert, changed in place.</param>
    public static void Normalize(SpaceStop? warning, SpaceStop? error)
    {
        if (warning is null || error is null || !warning.Enabled || !error.Enabled)
        {
            return;
        }

        error.Unit = warning.Unit;
        if (warning.Unit == SpaceUnit.Percent)
        {
            if (error.Value < warning.Value + PercentStep)
            {
                error.Value = Math.Min(100, warning.Value + PercentStep);
                warning.Value = Math.Min(warning.Value, error.Value - PercentStep);
            }

            return;
        }

        if (error.Value > warning.Value - GigabyteStep)
        {
            error.Value = Math.Max(0, warning.Value - GigabyteStep);
            warning.Value = Math.Max(warning.Value, error.Value + GigabyteStep);
        }
    }

    /// <summary>
    /// Gets the alerts that apply to a disk: its own when it has them, else the global ones.
    /// </summary>
    /// <param name="config">The settings.</param>
    /// <param name="mountPoint">The disk's mount point.</param>
    /// <returns>The Warning and Error alerts, and whether they are the disk's own.</returns>
    public static (SpaceStop Warning, SpaceStop Error, bool Own) For(StorageConfiguration config, string mountPoint)
    {
        ArgumentNullException.ThrowIfNull(config);
        var path = MountPaths.Normalize(mountPoint);
        var own = config.DiskOverrides?.LastOrDefault(o => o is not null && string.Equals(MountPaths.Normalize(o.MountPoint), path, MountPaths.Comparison));
        return own is null
            ? (config.WarningStop, config.ErrorStop, false)
            : (own.WarningStop ?? new SpaceStop { Enabled = false }, own.ErrorStop ?? new SpaceStop { Enabled = false }, true);
    }
}
