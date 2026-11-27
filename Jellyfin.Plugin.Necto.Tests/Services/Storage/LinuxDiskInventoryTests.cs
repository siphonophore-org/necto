using System;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.Necto.Services.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

using Jellyfin.Plugin.Necto.Tests.Fakes.Storage;

namespace Jellyfin.Plugin.Necto.Tests.Services.Storage;

/// <summary>
/// Covers finding unformatted disks on Linux from a fake /sys, udev record folder, and mount table.
/// </summary>
public sealed class LinuxDiskInventoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "storage-linux-" + Guid.NewGuid().ToString("N"));

    public LinuxDiskInventoryTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sys", "block"));
        Directory.CreateDirectory(Path.Combine(_root, "udev"));
        File.WriteAllText(Path.Combine(_root, "mountinfo"), "26 1 8:1 / / rw - ext4 /dev/sda1 rw\n");
    }

    public void Dispose() => Directory.Delete(_root, true);

    private LinuxDiskInventory Inventory() => new(Path.Combine(_root, "sys"), Path.Combine(_root, "udev"), Path.Combine(_root, "mountinfo"), NullLogger.Instance);

    // Writes one disk or partition: its size in sectors, its device number, and udev's record of it.
    private void Device(string path, string number, long sectors, string? fileSystem, bool partition = false)
    {
        var folder = Path.Combine(_root, "sys", "block", path);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "size"), sectors + "\n");
        File.WriteAllText(Path.Combine(folder, "dev"), number + "\n");
        if (partition)
        {
            File.WriteAllText(Path.Combine(folder, "partition"), "1\n");
        }

        var record = "S:disk/by-id/x\n" + (fileSystem is null ? string.Empty : "E:ID_FS_TYPE=" + fileSystem + "\n");
        File.WriteAllText(Path.Combine(_root, "udev", "b" + number), record);
    }

    [Fact]
    public void ABlankDiskIsFoundWithItsModelAndSize()
    {
        Device("sdb", "8:16", 3907029168, null);
        Directory.CreateDirectory(Path.Combine(_root, "sys", "block", "sdb", "device"));
        File.WriteAllText(Path.Combine(_root, "sys", "block", "sdb", "device", "vendor"), "ATA     \n");
        File.WriteAllText(Path.Combine(_root, "sys", "block", "sdb", "device", "model"), "Samsung SSD 870 \n");

        var disk = Assert.Single(Inventory().GetUnformatted());

        Assert.Equal("/dev/sdb", disk.Device);
        Assert.Equal("Samsung SSD 870", disk.Model);
        Assert.Equal(3907029168L * 512, disk.SizeBytes);
    }

    [Fact]
    public void ADiskWithAFormattedPartitionIsLeftOut()
    {
        Device("sdc", "8:32", 1000, null);
        Device("sdc/sdc1", "8:33", 900, "ext4", partition: true);

        Assert.Empty(Inventory().GetUnformatted());
    }

    [Fact]
    public void APartitionedDiskIsLeftOutEvenWithNoFileSystemOnIt()
    {
        // An encrypted partition has no signature udev knows, but it holds data.
        Device("sdd", "8:48", 1000, null);
        Device("sdd/sdd1", "8:49", 900, null, partition: true);

        Assert.Empty(Inventory().GetUnformatted());
    }

    [Fact]
    public void ADiskWithAPartitionTableAndNoPartitionsIsLeftOut()
    {
        Device("sdf", "8:80", 1000, null);
        File.AppendAllText(Path.Combine(_root, "udev", "b8:80"), "E:ID_PART_TABLE_TYPE=gpt\n");

        Assert.Empty(Inventory().GetUnformatted());
    }

    [Fact]
    public void ADiskSomethingIsStackedOnIsLeftOut()
    {
        Device("sdg", "8:96", 1000, null);
        Directory.CreateDirectory(Path.Combine(_root, "sys", "block", "sdg", "holders", "dm-0"));

        Assert.Empty(Inventory().GetUnformatted());
    }

    [Fact]
    public void TheBootAreasOfAnEmmcChipAreLeftOut()
    {
        Device("mmcblk0boot0", "179:8", 1000, null);
        Device("mmcblk0rpmb", "179:24", 1000, null);

        Assert.Empty(Inventory().GetUnformatted());
    }

    [Fact]
    public void AMountedDiskIsLeftOutEvenWithoutAFileSystemRecord()
    {
        Device("sda", "8:0", 1000, null);
        Device("sda/sda1", "8:1", 900, null, partition: true);

        Assert.Empty(Inventory().GetUnformatted());
    }

    [Fact]
    public void LoopAndOpticalDevicesAreLeftOut()
    {
        Device("loop0", "7:0", 1000, null);
        Device("sr0", "11:0", 1000, null);

        Assert.Empty(Inventory().GetUnformatted());
    }

    [Fact]
    public void ADiskUdevHasNoRecordOfIsLeftOut()
    {
        Device("sde", "8:64", 1000, null);
        File.Delete(Path.Combine(_root, "udev", "b8:64"));

        Assert.Empty(Inventory().GetUnformatted());
    }

    [Fact]
    public void WithoutUdevNothingIsReported()
    {
        Device("sdb", "8:16", 1000, null);
        Directory.Delete(Path.Combine(_root, "udev"), true);

        Assert.Empty(Inventory().GetUnformatted());
    }

    [Fact]
    public void AnInstallerDriveThatIsNotMountedIsListedWithWhatIsOnIt()
    {
        // The USB drive on the Debian test server: an installer image with two partitions, nothing mounted.
        Device("sdb", "8:16", 1000, "iso9660");
        File.AppendAllText(Path.Combine(_root, "udev", "b8:16"), "E:ID_PART_TABLE_TYPE=dos\n");
        Device("sdb/sdb1", "8:17", 800, "iso9660", partition: true);
        File.AppendAllText(Path.Combine(_root, "udev", "b8:17"), "E:ID_FS_LABEL_ENC=Debian\\x2013.3.0\\x20amd64\\x20n\n");
        Device("sdb/sdb2", "8:18", 10, "vfat", partition: true);

        var disk = Assert.Single(Inventory().GetUnmounted());

        Assert.Equal("/dev/sdb", disk.Device);
        Assert.False(disk.Blank);
        Assert.Equal(new[] { "iso9660", "Debian 13.3.0 amd64 n (iso9660)", "vfat" }, disk.Contents);
        Assert.Empty(Inventory().GetUnformatted());
    }

    [Fact]
    public void APartitionWithNothingUdevKnowsStillHoldsData()
    {
        Device("sdh", "8:112", 1000, null);
        Device("sdh/sdh1", "8:113", 900, null, partition: true);

        Assert.Equal(new[] { "1 partition" }, Assert.Single(Inventory().GetUnmounted()).Contents);
    }

    [Fact]
    public void ADiskInUseAsSwapIsLeftOut()
    {
        Device("sdi", "8:128", 1000, "swap");
        Directory.CreateDirectory(Path.Combine(_root, "sys", "class", "block", "sdi"));
        File.WriteAllText(Path.Combine(_root, "sys", "class", "block", "sdi", "dev"), "8:128\n");
        File.WriteAllText(Path.Combine(_root, "swaps"), "Filename Type Size Used Priority\n/dev/sdi partition 1000 0 -2\n");

        Assert.Empty(Inventory().GetUnmounted());
    }

    [Fact]
    public void AnEncodedLabelIsReadBack() => Assert.Equal("My Disk é", LinuxDiskInventory.Unescape("My\\x20Disk\\x20\\xc3\\xa9"));

    [Fact]
    public void AUsbDiskIsExternal()
    {
        Device("sdj", "8:144", 1000, null);
        File.AppendAllText(Path.Combine(_root, "udev", "b8:144"), "E:ID_BUS=usb\n");

        Assert.True(Assert.Single(Inventory().GetUnmounted()).External);
    }

    [Fact]
    public void AMountedUsbPartitionIsExternalByItsDisk()
    {
        Device("sdk", "8:160", 1000, null);
        File.AppendAllText(Path.Combine(_root, "udev", "b8:160"), "E:ID_BUS=usb\n");
        Device("sdk/sdk1", "8:161", 900, "ext4", partition: true);
        Directory.CreateDirectory(Path.Combine(_root, "sys", "dev", "block"));
        Directory.CreateSymbolicLink(Path.Combine(_root, "sys", "dev", "block", "8:161"), Path.Combine(_root, "sys", "block", "sdk", "sdk1"));
        File.AppendAllText(Path.Combine(_root, "mountinfo"), "40 1 8:161 / /mnt/usb\\040drive rw - ext4 /dev/sdk1 rw\n");

        Assert.True(Inventory().IsExternal("/mnt/usb drive"));
        Assert.False(Inventory().IsExternal("/"));
    }

    [Fact]
    public void AMountedBtrfsDiskIsLeftOutThoughItsNumberIsMadeUp()
    {
        // Btrfs mounts under an anonymous device number, 0:45 here, so only its source names the partition.
        Device("sdl", "8:176", 1000, null);
        Device("sdl/sdl1", "8:177", 900, "btrfs", partition: true);
        File.AppendAllText(Path.Combine(_root, "mountinfo"), "50 1 0:45 / /mnt/data rw - btrfs /dev/sdl1 rw\n");

        Assert.Empty(Inventory().GetUnmounted());
    }

    [Fact]
    public void ADiskFstabMountsSaysWhere()
    {
        Device("sdm", "8:192", 1000, null);
        Device("sdm/sdm1", "8:193", 900, "ext4", partition: true);
        File.AppendAllText(Path.Combine(_root, "udev", "b8:193"), "E:ID_FS_UUID=1234-abcd\n");
        File.WriteAllText(Path.Combine(_root, "fstab"), "# comment\nUUID=1234-abcd /mnt/my\\040media ext4 defaults,nofail 0 2\n");

        Assert.Equal("/mnt/my media", Assert.Single(Inventory().GetUnmounted()).MountsAt);
        Assert.True(Inventory().HasFstabEntry("/mnt/my media"));
        Assert.False(Inventory().HasFstabEntry("/mnt/other"));
    }

    [Theory]
    [InlineData("/mnt/my\\040media", "/mnt/my media")]
    [InlineData("/mnt/tab\\011here", "/mnt/tab\there")]
    [InlineData("/mnt/plain", "/mnt/plain")]
    [InlineData("/mnt/end\\04", "/mnt/end\\04")]
    public void AMountTablePathIsDecoded(string written, string path) => Assert.Equal(path, LinuxDiskInventory.UnescapeMount(written));
}
