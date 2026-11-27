using System;
using System.Buffers.Binary;
using System.Text;
using Jellyfin.Plugin.Necto.Services.Storage;
using Xunit;

using Jellyfin.Plugin.Necto.Tests.Fakes.Storage;

namespace Jellyfin.Plugin.Necto.Tests.Services.Storage;

/// <summary>
/// Covers reading Windows' partition layout and storage descriptor, built here byte for byte as Windows returns them.
/// </summary>
public class WindowsDiskLayoutTests
{
    private static readonly Guid MicrosoftReserved = new("e3c9e316-0b5c-4db8-817d-f92df00215ae");
    private static readonly Guid BasicData = new("ebd0a0a2-b9e5-4433-87c0-68b6b72699c7");

    // DRIVE_LAYOUT_INFORMATION_EX: style, count, a 40 byte union, then 144 byte entries.
    private static byte[] Layout(int style, int count)
    {
        var buffer = new byte[48 + (count * 144)];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, style);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4), count);
        return buffer;
    }

    private static byte[] Mbr(params byte[] types)
    {
        var buffer = Layout(0, types.Length);
        for (var i = 0; i < types.Length; i++)
        {
            buffer[48 + (i * 144) + 32] = types[i];
        }

        return buffer;
    }

    private static byte[] Gpt(params Guid[] types)
    {
        var buffer = Layout(1, types.Length);
        for (var i = 0; i < types.Length; i++)
        {
            types[i].TryWriteBytes(buffer.AsSpan(48 + (i * 144) + 32, 16));
        }

        return buffer;
    }

    [Fact]
    public void ADiskNeverInitializedIsEmpty() => Assert.True(WindowsDiskLayout.IsEmpty(Layout(2, 0)));

    [Fact]
    public void AnMbrDiskWithFourEmptySlotsIsEmpty() => Assert.True(WindowsDiskLayout.IsEmpty(Mbr(0, 0, 0, 0)));

    [Fact]
    public void AnMbrDiskWithAnNtfsPartitionIsNotEmpty() => Assert.False(WindowsDiskLayout.IsEmpty(Mbr(0x07, 0, 0, 0)));

    [Fact]
    public void AGptDiskWithOnlyTheReservedPartitionIsEmpty() => Assert.True(WindowsDiskLayout.IsEmpty(Gpt(MicrosoftReserved)));

    [Fact]
    public void AGptDiskWithNoPartitionsIsEmpty() => Assert.True(WindowsDiskLayout.IsEmpty(Gpt()));

    [Fact]
    public void AGptDiskWithADataPartitionIsNotEmpty() => Assert.False(WindowsDiskLayout.IsEmpty(Gpt(MicrosoftReserved, BasicData)));

    [Fact]
    public void ACutOffLayoutCannotBeRead()
    {
        var layout = Gpt(BasicData);

        Assert.Null(WindowsDiskLayout.IsEmpty(layout.AsSpan(0, 100)));
        Assert.Null(WindowsDiskLayout.IsEmpty(new byte[4]));
    }

    [Fact]
    public void TheModelJoinsVendorAndProductWithoutPadding()
    {
        // STORAGE_DEVICE_DESCRIPTOR: the vendor offset at 12 and the product offset at 16, each to an ANSI string.
        var buffer = new byte[96];
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(12), 40);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(16), 52);
        Encoding.ASCII.GetBytes("Samsung ").CopyTo(buffer, 40);
        Encoding.ASCII.GetBytes("SSD 870 EVO 2TB ").CopyTo(buffer, 52);

        Assert.Equal("Samsung SSD 870 EVO 2TB", WindowsDiskLayout.Model(buffer));
    }

    [Fact]
    public void ADescriptorWithNoVendorUsesTheProduct()
    {
        var buffer = new byte[64];
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(16), 32);
        Encoding.ASCII.GetBytes("WDC WD80EFAX").CopyTo(buffer, 32);

        Assert.Equal("WDC WD80EFAX", WindowsDiskLayout.Model(buffer));
    }

    [Fact]
    public void OffWindowsTheReaderFindsNothing()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Empty(new WindowsDiskInventory(Microsoft.Extensions.Logging.Abstractions.NullLogger<WindowsDiskInventory>.Instance).GetUnformatted());
    }

    [Theory]
    [InlineData(7, 0, true)]
    [InlineData(4, 0, true)]
    [InlineData(12, 0, true)]
    [InlineData(17, 0, false)]
    [InlineData(11, 1, true)]
    public void AUsbOrRemovableDiskIsExternal(int bus, byte removable, bool external)
    {
        var descriptor = new byte[40];
        descriptor[10] = removable;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(descriptor.AsSpan(28), bus);
        Assert.Equal(external, WindowsDiskLayout.IsExternal(descriptor));
    }
}
