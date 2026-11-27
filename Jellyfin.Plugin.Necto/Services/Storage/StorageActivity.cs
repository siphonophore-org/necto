using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Services.Notifications;
using MediaBrowser.Controller.Configuration;
using Microsoft.Extensions.Logging;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// Writes disk changes and blocked scans to Jellyfin's activity log, in the server's display language, and sends each
/// alert to Notifications. The changes of one check are summed into one entry per kind, so a NAS going down with
/// three shares writes one entry and sends one event.
/// </summary>
public sealed class StorageActivity
{
    private readonly ActivityLogger _logger;
    private readonly Func<string?> _language;
    private readonly INotifier? _notifier;

    /// <summary>
    /// Initializes a new instance of the <see cref="StorageActivity"/> class, writing in the server's display language.
    /// </summary>
    /// <param name="logger">The activity log writer.</param>
    /// <param name="config">The server configuration, read for its display language each time an entry is written.</param>
    /// <param name="notifier">Sends each alert to Notifications.</param>
    public StorageActivity(ActivityLogger logger, IServerConfigurationManager config, INotifier notifier)
        : this(logger, () => StolonTranslations.ServerLanguage(config), notifier)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StorageActivity"/> class with a given language, for tests.
    /// </summary>
    /// <param name="logger">The activity log writer.</param>
    /// <param name="language">Gives the language entries are written in.</param>
    /// <param name="notifier">Sends each alert to Notifications, or <c>null</c> to send none.</param>
    public StorageActivity(ActivityLogger logger, Func<string?> language, INotifier? notifier = null)
    {
        _logger = logger;
        _language = language;
        _notifier = notifier;
    }

    /// <summary>
    /// Writes one entry for each kind of change in a check.
    /// </summary>
    /// <param name="events">The changes the check found.</param>
    public void Record(IReadOnlyList<DiskEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var language = _language();
        foreach (var group in events.GroupBy(e => e.Kind))
        {
            var list = group.ToList();
            var (type, nameOne, nameMany, severity) = group.Key switch
            {
                DiskEventKind.WentMissing => ("Storage.DiskMissing", TranslationKeys.StorageActivityNameDiskMissing, TranslationKeys.StorageActivityNameDisksMissing, LogLevel.Error),
                DiskEventKind.BecameUnresponsive => ("Storage.DiskUnresponsive", TranslationKeys.StorageActivityNameDiskUnresponsive, TranslationKeys.StorageActivityNameDisksUnresponsive, LogLevel.Warning),
                DiskEventKind.Returned => ("Storage.DiskBack", TranslationKeys.StorageActivityNameDiskBack, TranslationKeys.StorageActivityNameDisksBack, LogLevel.Information),
                DiskEventKind.Failing => ("Storage.DiskFailing", TranslationKeys.StorageActivityNameDiskFailing, TranslationKeys.StorageActivityNameDisksFailing, LogLevel.Error),
                DiskEventKind.Hot => ("Storage.DiskHot", TranslationKeys.StorageActivityNameDiskHot, TranslationKeys.StorageActivityNameDisksHot, LogLevel.Warning),
                DiskEventKind.Worn => ("Storage.DiskWorn", TranslationKeys.StorageActivityNameDiskWorn, TranslationKeys.StorageActivityNameDisksWorn, LogLevel.Warning),
                DiskEventKind.Low => ("Storage.DiskLow", TranslationKeys.StorageActivityNameDiskLow, TranslationKeys.StorageActivityNameDisksLow, LogLevel.Warning),
                _ => ("Storage.DiskFull", TranslationKeys.StorageActivityNameDiskFull, TranslationKeys.StorageActivityNameDisksFull, LogLevel.Error)
            };

            var name = StolonTranslations.Get(list.Count == 1 ? nameOne : nameMany, language);
            var overview = ActivityText.Sentence(Overview(group.Key, list, language));
            _logger.Log(name, type, overview, severity);
            Notify(StorageEvents.For(group.Key), name, overview, severity);
        }
    }

    /// <summary>
    /// Writes the entry for a library scan the scan guard stopped.
    /// </summary>
    /// <param name="blockers">The disks that blocked it.</param>
    public void ScanBlocked(IReadOnlyList<DiskInfo> blockers)
    {
        ArgumentNullException.ThrowIfNull(blockers);
        var language = _language();
        var name = StolonTranslations.Get(TranslationKeys.StorageActivityNameScanBlocked, language);
        var overview = ActivityText.Sentence(StolonTranslations.Format(TranslationKeys.StorageActivityScanBlocked, language, Names(blockers, language)));
        _logger.Log(name, "Storage.ScanBlocked", overview, LogLevel.Warning);
        Notify(StorageEvents.ScanBlocked, name, overview, LogLevel.Warning);
    }

    /// <summary>
    /// Writes the entry for disks mounted again, one entry for all of them.
    /// </summary>
    /// <param name="disks">The disks mounted again.</param>
    public void Remounted(IReadOnlyList<DiskInfo> disks)
    {
        ArgumentNullException.ThrowIfNull(disks);
        if (disks.Count == 0)
        {
            return;
        }

        var language = _language();
        var one = disks.Count == 1;
        var name = StolonTranslations.Get(one ? TranslationKeys.StorageActivityNameDiskRemounted : TranslationKeys.StorageActivityNameDisksRemounted, language);
        var overview = ActivityText.Sentence(StolonTranslations.Format(one ? TranslationKeys.StorageActivityDiskRemounted : TranslationKeys.StorageActivityDisksRemounted, language, Names(disks, language)));
        _logger.Log(name, "Storage.DiskRemounted", overview, LogLevel.Information);
        Notify(StorageEvents.DiskRemounted, name, overview, LogLevel.Information);
    }

    /// <summary>
    /// Writes the entry for disks whose first try to mount them again failed, one entry for all of them. Later
    /// tries that fail are not written, so a disk that stays gone writes once.
    /// </summary>
    /// <param name="failed">The disks, each with what came of its try.</param>
    public void NotRemounted(IReadOnlyList<(DiskInfo Disk, RemountResult Result)> failed)
    {
        ArgumentNullException.ThrowIfNull(failed);
        if (failed.Count == 0)
        {
            return;
        }

        var language = _language();
        var one = failed.Count == 1;
        var overview = one
            ? StolonTranslations.Format(TranslationKeys.StorageActivityDiskNotRemounted, language, Label(failed[0].Disk, language), Reason(failed[0].Result, language))
            : StolonTranslations.Format(TranslationKeys.StorageActivityDisksNotRemounted, language, Names(failed.Select(f => f.Disk), language));
        var name = StolonTranslations.Get(one ? TranslationKeys.StorageActivityNameDiskNotRemounted : TranslationKeys.StorageActivityNameDisksNotRemounted, language);
        _logger.Log(name, "Storage.DiskNotRemounted", ActivityText.Sentence(overview), LogLevel.Error);
        Notify(StorageEvents.RemountFailed, name, ActivityText.Sentence(overview), LogLevel.Error);
    }

    /// <summary>
    /// Writes the entry for a disk formatted and mounted.
    /// </summary>
    /// <param name="disk">The disk, as it was listed before.</param>
    /// <param name="fileSystem">The file system it was formatted as.</param>
    /// <param name="mountPoint">Where it was mounted, when known.</param>
    /// <param name="library">The library it was added to, or <c>null</c>.</param>
    public void Formatted(RawDisk disk, string fileSystem, string? mountPoint, string? library)
    {
        ArgumentNullException.ThrowIfNull(disk);
        var language = _language();
        var at = mountPoint ?? disk.Device;
        var overview = library is null
            ? StolonTranslations.Format(TranslationKeys.StorageActivityDiskFormatted, language, DiskName(disk), FileSystemName(fileSystem), at)
            : StolonTranslations.Format(TranslationKeys.StorageActivityDiskFormattedLibrary, language, DiskName(disk), FileSystemName(fileSystem), at, library);
        _logger.Log(StolonTranslations.Get(TranslationKeys.StorageActivityNameDiskFormatted, language), "Storage.DiskFormatted", ActivityText.Sentence(overview), LogLevel.Information);
    }

    /// <summary>
    /// Writes the entry for a folder made on a disk.
    /// </summary>
    /// <param name="path">The folder.</param>
    /// <param name="library">The library it was added to, or <c>null</c>.</param>
    public void FolderCreated(string path, string? library)
    {
        var language = _language();
        var overview = library is null
            ? StolonTranslations.Format(TranslationKeys.StorageActivityFolderCreated, language, path)
            : StolonTranslations.Format(TranslationKeys.StorageActivityFolderCreatedLibrary, language, path, library);
        _logger.Log(StolonTranslations.Get(TranslationKeys.StorageActivityNameFolderCreated, language), "Storage.FolderCreated", ActivityText.Sentence(overview), LogLevel.Information);
    }

    /// <summary>
    /// Writes the entry for a disk the OS could not format.
    /// </summary>
    /// <param name="disk">The disk.</param>
    /// <param name="error">What the OS said.</param>
    public void NotFormatted(RawDisk disk, string error)
    {
        ArgumentNullException.ThrowIfNull(disk);
        var language = _language();
        var name = StolonTranslations.Get(TranslationKeys.StorageActivityNameDiskNotFormatted, language);
        var overview = ActivityText.Sentence(StolonTranslations.Format(TranslationKeys.StorageActivityDiskNotFormatted, language, DiskName(disk), (error ?? string.Empty).TrimEnd('.')));
        _logger.Log(name, "Storage.DiskNotFormatted", overview, LogLevel.Error);
        Notify(StorageEvents.FormatFailed, name, overview, LogLevel.Error);
    }

    /// <summary>
    /// Names an unformatted disk by its model and device, such as Samsung SSD 870 (/dev/sdb).
    /// </summary>
    /// <param name="disk">The disk.</param>
    /// <returns>The name.</returns>
    public static string DiskName(RawDisk disk)
    {
        ArgumentNullException.ThrowIfNull(disk);
        return string.IsNullOrEmpty(disk.Model) ? disk.Device : disk.Model + " (" + disk.Device + ")";
    }

    /// <summary>
    /// Spells a file system as its makers do, such as exFAT or APFS.
    /// </summary>
    /// <param name="fileSystem">The file system as the plugin names it, such as exfat.</param>
    /// <returns>The name.</returns>
    public static string FileSystemName(string fileSystem) => fileSystem switch
    {
        "exfat" => "exFAT",
        "btrfs" => "Btrfs",
        _ => (fileSystem ?? string.Empty).ToUpperInvariant()
    };

    /// <summary>
    /// Says why a disk could not be mounted again.
    /// </summary>
    /// <param name="result">What came of the try.</param>
    /// <param name="language">The language.</param>
    /// <returns>The reason.</returns>
    public static string Reason(RemountResult result, string? language)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Failure switch
        {
            RemountFailure.Denied => StolonTranslations.Get(TranslationKeys.StorageRemountDenied, language),
            RemountFailure.SignIn => StolonTranslations.Get(TranslationKeys.StorageRemountSignIn, language),
            RemountFailure.Unreachable => StolonTranslations.Get(TranslationKeys.StorageRemountUnreachable, language),
            RemountFailure.NotMounted => StolonTranslations.Get(TranslationKeys.StorageRemountNotMounted, language),
            _ => StolonTranslations.Format(TranslationKeys.StorageRemountError, language, result.Code.ToString(CultureInfo.InvariantCulture))
        };
    }

    /// <summary>
    /// Names a disk for an overview: its name and mount point, or the mount point alone when they are the same. The
    /// system disk is named as such.
    /// </summary>
    /// <param name="disk">The disk.</param>
    /// <param name="language">The language.</param>
    /// <returns>The name.</returns>
    public static string Label(DiskInfo disk, string? language)
    {
        ArgumentNullException.ThrowIfNull(disk);

        // The system disk is called that, as on the Storage tab, rather than / or a volume label.
        if (disk.IsSystem)
        {
            return StolonTranslations.Format(TranslationKeys.StorageActivityDiskAt, language, StolonTranslations.Get(TranslationKeys.StorageActivitySystemDisk, language), disk.MountPoint);
        }

        return string.Equals(disk.Name, disk.MountPoint, StringComparison.Ordinal)
            ? disk.MountPoint
            : StolonTranslations.Format(TranslationKeys.StorageActivityDiskAt, language, disk.Name, disk.MountPoint);
    }

    /// <summary>
    /// Says how long a disk was away, in minutes, hours, or days.
    /// </summary>
    /// <param name="away">How long.</param>
    /// <param name="language">The language.</param>
    /// <returns>The length.</returns>
    public static string Duration(TimeSpan away, string? language)
    {
        if (away.TotalHours < 1)
        {
            var minutes = Math.Max(1, (int)Math.Round(away.TotalMinutes));
            return minutes == 1 ? StolonTranslations.Get(TranslationKeys.StorageDurationMinute, language) : StolonTranslations.Format(TranslationKeys.StorageDurationMinutes, language, minutes);
        }

        if (away.TotalHours < 48)
        {
            var hours = (int)Math.Round(away.TotalHours);
            return hours == 1 ? StolonTranslations.Get(TranslationKeys.StorageDurationHour, language) : StolonTranslations.Format(TranslationKeys.HoursMany, language, hours);
        }

        return StolonTranslations.Format(TranslationKeys.DayCount, language, (int)Math.Round(away.TotalDays));
    }

    private static string Overview(DiskEventKind kind, List<DiskEvent> events, string? language)
    {
        var disks = events.Select(e => e.Disk).ToList();
        if (events.Count > 1)
        {
            var many = kind switch
            {
                DiskEventKind.WentMissing => TranslationKeys.StorageActivityDisksMissing,
                DiskEventKind.BecameUnresponsive => TranslationKeys.StorageActivityDisksUnresponsive,
                DiskEventKind.Returned => TranslationKeys.StorageActivityDisksBack,
                DiskEventKind.Failing => TranslationKeys.StorageActivityDisksFailing,
                DiskEventKind.Hot => TranslationKeys.StorageActivityDisksHot,
                DiskEventKind.Worn => TranslationKeys.StorageActivityDisksWorn,
                DiskEventKind.Low => TranslationKeys.StorageActivityDisksLow,
                _ => TranslationKeys.StorageActivityDisksFull
            };
            return StolonTranslations.Format(many, language, Names(disks, language));
        }

        var one = events[0];
        var label = Label(one.Disk, language);
        return kind switch
        {
            DiskEventKind.WentMissing => StolonTranslations.Format(TranslationKeys.StorageActivityDiskMissing, language, label),
            DiskEventKind.BecameUnresponsive => StolonTranslations.Format(TranslationKeys.StorageActivityDiskUnresponsive, language, label),
            DiskEventKind.Failing => StolonTranslations.Format(TranslationKeys.StorageActivityDiskFailing, language, label),
            DiskEventKind.Hot => StolonTranslations.Format(TranslationKeys.StorageActivityDiskHot, language, label, (one.Disk.TemperatureCelsius ?? 0).ToString(CultureInfo.InvariantCulture)),
            DiskEventKind.Worn => StolonTranslations.Format(TranslationKeys.StorageActivityDiskWorn, language, label, (one.Disk.WearPercent ?? 0).ToString(CultureInfo.InvariantCulture)),
            DiskEventKind.Returned => one.Away is { } away
                ? StolonTranslations.Format(TranslationKeys.StorageActivityDiskBackAfter, language, label, Duration(away, language))
                : StolonTranslations.Format(TranslationKeys.StorageActivityDiskBack, language, label),
            _ => StolonTranslations.Format(TranslationKeys.StorageActivityDiskFullness, language, label, Percent(one.Disk), FormatUtilities.FormatBytes(one.Disk.FreeBytes ?? 0))
        };
    }

    /// <summary>
    /// Sends one alert to Notifications, without waiting for its channels.
    /// </summary>
    /// <param name="key">The event's key, from <see cref="StorageEvents"/>.</param>
    /// <param name="title">The entry's name.</param>
    /// <param name="body">The entry's overview.</param>
    /// <param name="severity">How much it matters.</param>
    private void Notify(string key, string title, string? body, LogLevel severity)
    {
        if (_notifier is null)
        {
            return;
        }

        var level = severity switch
        {
            LogLevel.Error or LogLevel.Critical => NectoEventLevel.Error,
            LogLevel.Warning => NectoEventLevel.Warning,
            _ => NectoEventLevel.Info
        };
        _ = SendAsync(new NectoEvent(key, title, body ?? string.Empty) { Level = level });
    }

    /// <summary>
    /// Sends an event, keeping a failure from reaching the check that raised it.
    /// </summary>
    /// <param name="nectoEvent">The event.</param>
    /// <returns>A task that completes once the event is sent.</returns>
    private async Task SendAsync(NectoEvent nectoEvent)
    {
        try
        {
            await _notifier!.SendAsync(nectoEvent).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // Notifications never throws for a channel, so anything left is not the check's to handle.
        }
    }

    private static string Names(IEnumerable<DiskInfo> disks, string? language)
        => string.Join(", ", disks.Select(d => Label(d, language)));

    private static string Percent(DiskInfo disk)
    {
        var total = disk.TotalBytes ?? 0;
        var used = total > 0 ? (total - (disk.FreeBytes ?? 0)) * 100d / total : 0;
        return Math.Round(used).ToString(CultureInfo.InvariantCulture);
    }
}
