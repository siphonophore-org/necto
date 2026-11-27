using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Services.Storage;

namespace Jellyfin.Plugin.Necto.Services.Notifications;

/// <summary>
/// The keys of every event Storage sends, so Notifications can list them.
/// </summary>
public static class StorageEvents
{
    /// <summary>A disk a library uses went missing.</summary>
    public const string DiskMissing = "storage.disk_missing";

    /// <summary>A disk stopped answering in time.</summary>
    public const string DiskUnresponsive = "storage.disk_unresponsive";

    /// <summary>A missing or unresponsive disk is back.</summary>
    public const string DiskBack = "storage.disk_back";

    /// <summary>A missing disk was mounted again.</summary>
    public const string DiskRemounted = "storage.disk_remounted";

    /// <summary>A missing disk could not be mounted again.</summary>
    public const string RemountFailed = "storage.remount_failed";

    /// <summary>A disk's SMART data says it is failing, too hot, or worn.</summary>
    public const string SmartWarning = "storage.smart_warning";

    /// <summary>A disk passed its warning point.</summary>
    public const string SpaceLow = "storage.space_low";

    /// <summary>A disk passed its error point.</summary>
    public const string SpaceFull = "storage.space_full";

    /// <summary>A library scan was blocked while a disk was missing.</summary>
    public const string ScanBlocked = "storage.scan_blocked";

    /// <summary>A disk could not be formatted.</summary>
    public const string FormatFailed = "storage.format_failed";

    /// <summary>
    /// Gets every key Storage sends, in the order Notifications lists them.
    /// </summary>
    public static IReadOnlyList<string> All { get; } = new[]
    {
        DiskMissing,
        DiskUnresponsive,
        DiskBack,
        DiskRemounted,
        RemountFailed,
        SmartWarning,
        SpaceLow,
        SpaceFull,
        ScanBlocked,
        FormatFailed
    };

    /// <summary>
    /// Picks the key of a disk change.
    /// </summary>
    /// <param name="kind">The change.</param>
    /// <returns>The event's key.</returns>
    public static string For(DiskEventKind kind) => kind switch
    {
        DiskEventKind.WentMissing => DiskMissing,
        DiskEventKind.BecameUnresponsive => DiskUnresponsive,
        DiskEventKind.Returned => DiskBack,
        DiskEventKind.Failing or DiskEventKind.Hot or DiskEventKind.Worn => SmartWarning,
        DiskEventKind.Low => SpaceLow,
        _ => SpaceFull
    };
}
