using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using Jellyfin.Plugin.Necto.Configuration.Storage;
using Jellyfin.Plugin.Necto.Services.Storage;
using Tmds.DBus.Protocol;
using Xunit;

using Jellyfin.Plugin.Necto.Tests.Fakes.Storage;

namespace Jellyfin.Plugin.Necto.Tests.Services.Storage;

/// <summary>
/// Covers reading SMART data: the NVMe health log, udisks2's ATA and NVMe properties, D-Bus value conversion, and the
/// Windows request and reply around the NVMe log.
/// </summary>
public class SmartTests
{
    // A health log with a critical warning byte, a temperature in kelvin, wear, and power-on hours.
    private static byte[] Log(byte warning, ushort kelvin, byte wear, long hours)
    {
        var log = new byte[NvmeSmartLog.Size];
        log[0] = warning;
        BinaryPrimitives.WriteUInt16LittleEndian(log.AsSpan(1), kelvin);
        log[5] = wear;
        BinaryPrimitives.WriteInt64LittleEndian(log.AsSpan(128), hours);
        return log;
    }

    [Fact]
    public void AHealthyNvmeLogIsGood()
    {
        var reading = NvmeSmartLog.Parse(Log(0, 304, 3, 2448))!;

        Assert.Equal(new SmartReading(DiskHealth.Good, 31, 2448, 3), reading);
    }

    [Fact]
    public void AnyCriticalWarningIsFailing() => Assert.Equal(DiskHealth.Failing, NvmeSmartLog.Parse(Log(0x01, 304, 100, 1))!.Health);

    [Fact]
    public void AShortLogIsNoReading() => Assert.Null(NvmeSmartLog.Parse(new byte[100]));

    // udisks2's objects for one disk: its filesystem mounted at /mnt/media, its block device, and its drive.
    private static Dictionary<string, Dictionary<string, Dictionary<string, object?>>> Udisks(string driveInterface, Dictionary<string, object?> drive)
        => new()
        {
            ["/org/freedesktop/UDisks2/block_devices/sdb1"] = new()
            {
                ["org.freedesktop.UDisks2.Block"] = new() { ["Drive"] = "/org/freedesktop/UDisks2/drives/WDC" },
                ["org.freedesktop.UDisks2.Filesystem"] = new() { ["MountPoints"] = new List<object?> { Encoding.UTF8.GetBytes("/mnt/media\0") } }
            },
            ["/org/freedesktop/UDisks2/drives/WDC"] = new() { [driveInterface] = drive }
        };

    private static Dictionary<string, object?> Ata(bool failing) => new()
    {
        ["SmartSupported"] = true,
        ["SmartEnabled"] = true,
        ["SmartUpdated"] = 1700000000UL,
        ["SmartFailing"] = failing,
        ["SmartTemperature"] = 311.15,
        ["SmartPowerOnSeconds"] = 3600UL * 41210
    };

    [Fact]
    public void AnAtaDriveReportsItsHealth()
    {
        var reading = UdisksSmart.Find(Udisks("org.freedesktop.UDisks2.Drive.Ata", Ata(false)), "/mnt/media");

        Assert.Equal(new SmartReading(DiskHealth.Good, 38, 41210), reading);
    }

    [Fact]
    public void AFailingAtaDriveIsFailing()
        => Assert.Equal(DiskHealth.Failing, UdisksSmart.Find(Udisks("org.freedesktop.UDisks2.Drive.Ata", Ata(true)), "/mnt/media")!.Health);

    [Fact]
    public void AnAtaDriveUdisksNeverReadHasNoReading()
    {
        var ata = Ata(false);
        ata["SmartUpdated"] = 0UL;

        Assert.Null(UdisksSmart.Find(Udisks("org.freedesktop.UDisks2.Drive.Ata", ata), "/mnt/media"));
    }

    [Fact]
    public void AnNvmeDriveWithAWarningIsFailing()
    {
        var nvme = new Dictionary<string, object?>
        {
            ["SmartUpdated"] = 1700000000UL,
            ["SmartCriticalWarning"] = new List<object?> { "spare" },
            ["SmartTemperature"] = 320UL,
            ["SmartPowerOnHours"] = 900UL
        };

        Assert.Equal(new SmartReading(DiskHealth.Failing, 47, 900), UdisksSmart.Find(Udisks("org.freedesktop.UDisks2.NVMe.Controller", nvme), "/mnt/media"));
    }

    [Fact]
    public void AMountPointUdisksDoesNotKnowHasNoReading()
        => Assert.Null(UdisksSmart.Find(Udisks("org.freedesktop.UDisks2.Drive.Ata", Ata(false)), "/mnt/other"));

    [Fact]
    public void DBusValuesBecomePlainValues()
    {
        Assert.Equal(true, LinuxSmartReader.Plain(VariantValue.Bool(true)));
        Assert.Equal(320UL, LinuxSmartReader.Plain(VariantValue.UInt16(320)));
        Assert.Equal(311.15, LinuxSmartReader.Plain(VariantValue.Double(311.15)));
        Assert.Equal("/org/x", LinuxSmartReader.Plain(VariantValue.ObjectPath(new ObjectPath("/org/x"))));
        Assert.Equal(new byte[] { 47, 0 }, LinuxSmartReader.Plain(VariantValue.Array(new byte[] { 47, 0 })));
        Assert.Equal(new List<object?> { "spare" }, LinuxSmartReader.Plain(VariantValue.Array(new[] { "spare" })));
    }

    [Fact]
    public void TheWindowsRequestAsksForTheNvmeSmartLog()
    {
        var request = WindowsSmartReader.NvmeLogRequest();

        Assert.Equal(8 + 40 + 512, request.Length);
        Assert.Equal(50, BinaryPrimitives.ReadInt32LittleEndian(request));
        Assert.Equal(3, BinaryPrimitives.ReadInt32LittleEndian(request.AsSpan(8)));
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(request.AsSpan(16)));
        Assert.Equal(40, BinaryPrimitives.ReadInt32LittleEndian(request.AsSpan(24)));
        Assert.Equal(512, BinaryPrimitives.ReadInt32LittleEndian(request.AsSpan(28)));
    }

    [Fact]
    public void TheWindowsReplyHoldsTheLogAfterTheProtocolData()
    {
        var reply = WindowsSmartReader.NvmeLogRequest();
        Log(0, 310, 7, 1234).CopyTo(reply, 48);

        Assert.Equal(new SmartReading(DiskHealth.Good, 37, 1234, 7), WindowsSmartReader.ParseNvmeReply(reply));
        Assert.Null(WindowsSmartReader.ParseNvmeReply(new byte[20]));
    }
}
