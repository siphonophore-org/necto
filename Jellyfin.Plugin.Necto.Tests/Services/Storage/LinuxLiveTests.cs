using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Jellyfin.Plugin.Necto.Services.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

using Jellyfin.Plugin.Necto.Tests.Fakes.Storage;

namespace Jellyfin.Plugin.Necto.Tests.Services.Storage;

/// <summary>
/// On a Linux machine with systemd, mounts and remounts a real /etc/fstab entry through systemd over D-Bus, and runs
/// the Linux readers against the real system. They run only with STORAGE_LIVE_LINUX=1, as tools/linux-live/run.sh
/// sets in a systemd container, since they change what is mounted. STORAGE_LIVE_MOUNT names the fstab entry's mount
/// point.
/// </summary>
public class LinuxLiveTests
{
    private readonly ITestOutputHelper _output;

    public LinuxLiveTests(ITestOutputHelper output) => _output = output;

    private static bool Live => OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("STORAGE_LIVE_LINUX") == "1";

    private static string MountPoint => Environment.GetEnvironmentVariable("STORAGE_LIVE_MOUNT") ?? "/mnt/storage-live";

    [Fact]
    public void AnUnmountedFstabEntryIsMountedAgain()
    {
        if (!Live)
        {
            return;
        }

        var remounter = new LinuxRemounter(NullLogger<LinuxRemounter>.Instance);
        Run("umount", MountPoint);
        Assert.False(IsMounted(MountPoint));

        var result = remounter.Remount(MountPoint, null);
        _output.WriteLine(result.ToString());

        Assert.True(result.Tried);
        Assert.True(result.Mounted);
        Assert.True(WaitForMount(MountPoint), MountPoint + " did not show up after the remount");
        Assert.Contains(MountPoint, new DriveMountTable(NullLogger<DriveMountTable>.Instance).GetMountPoints());
    }

    [Fact]
    public void AMountOutsideFstabIsLeftAlone()
    {
        if (!Live)
        {
            return;
        }

        var result = new LinuxRemounter(NullLogger<LinuxRemounter>.Instance).Remount("/mnt/storage-not-in-fstab", null);
        _output.WriteLine(result.ToString());

        Assert.False(result.Tried);
    }

    [Fact]
    public void TheLinuxReadersRunAgainstTheRealSystem()
    {
        if (!Live)
        {
            return;
        }

        var unformatted = new LinuxDiskInventory(NullLogger<LinuxDiskInventory>.Instance).GetUnformatted();
        var smart = new LinuxSmartReader(NullLogger<LinuxSmartReader>.Instance).Read("/");
        var mounts = new DriveMountTable(NullLogger<DriveMountTable>.Instance).GetMountPoints();
        _output.WriteLine("Unformatted: " + string.Join(", ", unformatted.Select(d => d.Device)));
        _output.WriteLine("SMART for /: " + (smart?.ToString() ?? "none"));
        _output.WriteLine("Mounts: " + string.Join(", ", mounts));

        Assert.Contains("/", mounts);
    }

    [Fact]
    public void ABlankDiskIsFormattedMountedAndAddedToFstab()
    {
        if (!Live)
        {
            return;
        }

        // A scratch loop device stands in for a new disk. The inventory leaves loop devices out, so the formatter is
        // given it directly.
        var image = Path.Combine(Path.GetTempPath(), "storage-live-" + Guid.NewGuid().ToString("N") + ".img");
        using (var file = File.Create(image))
        {
            file.SetLength(128L * 1024 * 1024);
        }

        var device = Output("losetup", "--find --show --partscan " + image).Trim();
        _output.WriteLine("Loop device: " + device);
        try
        {
            var formatter = new LinuxDiskFormatter(new OutputLogger<LinuxDiskFormatter>(_output));
            _output.WriteLine("File systems: " + string.Join(", ", formatter.FileSystems));
            Assert.Contains("ext4", formatter.FileSystems);

            var result = formatter.Format(new RawDisk(device, null, 128L * 1024 * 1024), "LiveTest", "ext4");
            _output.WriteLine(result.ToString());

            Assert.True(result.Formatted, result.Error);
            Assert.Equal("/mnt/LiveTest", result.MountPoint);
            Assert.True(IsMounted("/mnt/LiveTest"));
            Assert.Contains(File.ReadLines("/etc/fstab"), l => l.StartsWith("UUID=", StringComparison.Ordinal) && l.Contains("/mnt/LiveTest", StringComparison.Ordinal));

            // A disk that now holds data, unmounted, is erased and formatted again, as a used USB drive would be.
            // The old file system's fstab entry is removed with it, so the folder has one entry, the new one's.
            Run("umount", "/mnt/LiveTest");
            var again = formatter.Format(new RawDisk(device, null, 128L * 1024 * 1024, new[] { "LiveTest (ext4)" }), "LiveTest", "ext4");
            _output.WriteLine("Again: " + again);
            Assert.True(again.Formatted, again.Error);
            Assert.True(IsMounted("/mnt/LiveTest"));
            Assert.Single(File.ReadLines("/etc/fstab"), l => l.Contains("/mnt/LiveTest", StringComparison.Ordinal));
        }
        finally
        {
            Run("umount", "/mnt/LiveTest");
            Run("losetup", "-d " + device);
            File.Delete(image);
            File.WriteAllLines("/etc/fstab", File.ReadLines("/etc/fstab").Where(l => !l.Contains("/mnt/LiveTest", StringComparison.Ordinal)).ToList());
        }
    }

    [Fact]
    public void JellyfinsOwnUserFormatsADiskThroughPolkit()
    {
        // Run by tools/linux-live/run.sh as the jellyfin user, as Jellyfin runs on a real server, on a loop device it
        // sets up as root. Making the mount folder needs udisks' rights, not Jellyfin's.
        var device = Environment.GetEnvironmentVariable("STORAGE_LIVE_ROOTLESS_DEVICE");
        if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(device))
        {
            return;
        }

        var formatter = new LinuxDiskFormatter(new OutputLogger<LinuxDiskFormatter>(_output));
        var result = formatter.Format(new RawDisk(device, null, 128L * 1024 * 1024), "Rootless", "ext4");
        _output.WriteLine("Rootless: " + result);

        Assert.True(result.Formatted, result.Error);
        Assert.True(WaitForMount("/mnt/Rootless"));
        Assert.Single(File.ReadLines("/etc/fstab"), l => l.Contains("/mnt/Rootless", StringComparison.Ordinal));

        // Jellyfin's user can save metadata on the new disk, as it would an NFO file.
        File.WriteAllText("/mnt/Rootless/movie.nfo", "<movie />");
        Assert.True(new DriveMountTable(Microsoft.Extensions.Logging.Abstractions.NullLogger<DriveMountTable>.Instance).CanWrite("/mnt/Rootless"));

        // exFAT keeps no owners, so its fstab entry names Jellyfin's user, or it would mount owned by root.
        var second = Environment.GetEnvironmentVariable("STORAGE_LIVE_ROOTLESS_DEVICE2");
        if (string.IsNullOrEmpty(second))
        {
            return;
        }

        var exfat = formatter.Format(new RawDisk(second, null, 128L * 1024 * 1024), "RootlessX", "exfat");
        _output.WriteLine("Rootless exFAT: " + exfat);
        Assert.True(exfat.Formatted, exfat.Error);
        Assert.True(WaitForMount("/mnt/RootlessX"));
        File.WriteAllText("/mnt/RootlessX/movie.nfo", "<movie />");
        Assert.Contains(File.ReadLines("/etc/fstab"), l => l.Contains("/mnt/RootlessX", StringComparison.Ordinal) && l.Contains(",uid=", StringComparison.Ordinal));
    }

    private static bool IsMounted(string mountPoint)
        => File.ReadLines("/proc/self/mounts").Select(l => l.Split(' ')).Any(f => f.Length > 1 && f[1] == mountPoint);

    // systemd mounts after StartUnit returns, so the mount can take a moment to show.
    private static bool WaitForMount(string mountPoint)
    {
        for (var i = 0; i < 30; i++)
        {
            if (IsMounted(mountPoint))
            {
                return true;
            }

            Thread.Sleep(200);
        }

        return false;
    }

    private static string Output(string file, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(file, arguments) { RedirectStandardOutput = true });
        var text = process!.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return text;
    }

    private static void Run(string file, string argument)
    {
        using var process = Process.Start(new ProcessStartInfo(file, argument) { RedirectStandardError = true });
        process!.WaitForExit();
    }
}
