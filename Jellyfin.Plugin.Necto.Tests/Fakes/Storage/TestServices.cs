using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Necto.Configuration.Storage;
using Jellyfin.Plugin.Necto.Services.Notifications;
using Jellyfin.Plugin.Necto.Services.Storage;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Necto.Tests.Fakes.Storage;

/// <summary>
/// Shared stand-ins for the Storage tests: an activity log and a notifier that keep what they are given, settings a
/// test sets, Jellyfin's library and paths, and disks to feed the rules.
/// </summary>
internal static class TestServices
{
    /// <summary>
    /// Builds an activity writer over a stand-in activity log, in US English.
    /// </summary>
    /// <param name="log">The stand-in activity log, to read the entries back.</param>
    /// <returns>The writer.</returns>
    public static StorageActivity Activity(out FakeActivity log) => Activity(out log, out _);

    /// <summary>
    /// Builds an activity writer over a stand-in activity log and notifier, in US English.
    /// </summary>
    /// <param name="log">The stand-in activity log, to read the entries back.</param>
    /// <param name="notifier">The stand-in notifier, to read the events back.</param>
    /// <returns>The writer.</returns>
    public static StorageActivity Activity(out FakeActivity log, out RecordingNotifier notifier)
    {
        log = new FakeActivity();
        notifier = new RecordingNotifier();
        return new StorageActivity(log.Logger, () => "en-us", notifier);
    }

    /// <summary>
    /// Reads every entry written to a stand-in activity log.
    /// </summary>
    /// <param name="log">The stand-in activity log.</param>
    /// <returns>The entries, in the order they were written.</returns>
    public static List<ActivityLog> Entries(FakeActivity log)
    {
        lock (log.Entries)
        {
            return log.Entries.ToList();
        }
    }

    /// <summary>
    /// Builds a stand-in for Jellyfin's paths, with the plugin configurations in a folder.
    /// </summary>
    /// <param name="folder">The folder.</param>
    /// <returns>The paths.</returns>
    public static IApplicationPaths Paths(string folder)
    {
        var (paths, stub) = InterfaceStub.Create<IApplicationPaths>();
        stub.Answer("get_PluginConfigurationsPath", _ => folder);
        return paths;
    }

    /// <summary>
    /// Builds a stand-in for Jellyfin's library manager with the given libraries.
    /// </summary>
    /// <param name="folders">The libraries.</param>
    /// <param name="stub">The stub, to read the calls back.</param>
    /// <returns>The library manager.</returns>
    public static ILibraryManager Libraries(List<VirtualFolderInfo> folders, out InterfaceStub stub)
    {
        var (libraries, s) = InterfaceStub.Create<ILibraryManager>();
        s.Answer("GetVirtualFolders", _ => folders);
        s.Answer("AddMediaPath", _ => null);
        stub = s;
        return libraries;
    }

    /// <summary>
    /// Builds a stand-in for Jellyfin's real-time monitor.
    /// </summary>
    /// <param name="stub">The stub, to read the calls back.</param>
    /// <returns>The monitor.</returns>
    public static ILibraryMonitor Monitor(out InterfaceStub stub)
    {
        var (monitor, s) = InterfaceStub.Create<ILibraryMonitor>();
        stub = s;
        return monitor;
    }

    /// <summary>
    /// Counts the calls a stub took to one member with the first argument given.
    /// </summary>
    /// <param name="stub">The stub.</param>
    /// <param name="member">The member's name.</param>
    /// <param name="first">The first argument, or <c>null</c> for any.</param>
    /// <returns>How many calls matched.</returns>
    public static int Count(InterfaceStub stub, string member, object? first = null)
        => stub.Calls.Count(c => c.Name == member && (first is null || Equals(c.Args.FirstOrDefault(), first)));

    /// <summary>
    /// Builds a disk.
    /// </summary>
    /// <param name="mount">Its mount point.</param>
    /// <param name="state">Whether it can be read.</param>
    /// <param name="usedPercent">How full it is, when mounted.</param>
    /// <returns>The disk.</returns>
    public static DiskInfo Disk(string mount, DiskState state = DiskState.Available, double usedPercent = 50)
    {
        const long Total = 1000L * 1024 * 1024 * 1024;
        return new DiskInfo
        {
            MountPoint = mount,
            Name = MountPaths.DisplayName(mount, null),
            State = state,
            TotalBytes = state == DiskState.Available ? Total : null,
            FreeBytes = state == DiskState.Available ? (long)(Total * (100 - usedPercent) / 100) : null
        };
    }

    /// <summary>
    /// Settings a test can change.
    /// </summary>
    public sealed class Settings : StorageSettings
    {
        /// <summary>Gets the settings the test set.</summary>
        public StorageConfiguration Value { get; } = new() { Enabled = true };

        /// <inheritdoc />
        public override StorageConfiguration Current => Value;
    }

    /// <summary>
    /// A notifier that keeps every event it is given.
    /// </summary>
    public sealed class RecordingNotifier : INotifier
    {
        /// <summary>Gets the events, in the order they were sent.</summary>
        public List<NectoEvent> Events { get; } = new();

        /// <inheritdoc />
        public Task SendAsync(NectoEvent nectoEvent, CancellationToken cancellationToken = default)
        {
            lock (Events)
            {
                Events.Add(nectoEvent);
            }

            return Task.CompletedTask;
        }
    }
}
