using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Services.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

using Jellyfin.Plugin.Necto.Tests.Fakes.Storage;

namespace Jellyfin.Plugin.Necto.Tests.Services.Storage;

/// <summary>
/// Covers finding unformatted disks on macOS: which descriptions count as blank, which /dev entries are whole disks,
/// and, on a Mac, a real call into DiskArbitration.
/// </summary>
public class MacDiskInventoryTests
{
    private static MacDiskDescription Blank(string device = "disk20")
        => new(device, Whole: true, Leaf: true, Content: string.Empty, VolumeKind: null, Name: "Samsung SSD 870 EVO Media", Model: "Samsung SSD 870 EVO", SizeBytes: 2000);

    [Fact]
    public void AWholeEmptyDiskIsBlank() => Assert.True(Blank().IsBlank);

    [Theory]
    [InlineData(false, true, "", null)]
    [InlineData(true, false, "", null)]
    [InlineData(true, true, "GUID_partition_scheme", null)]
    [InlineData(true, true, "", "apfs")]
    public void ADiskThatHoldsAnythingIsNotBlank(bool whole, bool leaf, string content, string? kind)
        => Assert.False((Blank() with { Whole = whole, Leaf = leaf, Content = content, VolumeKind = kind }).IsBlank);

    [Fact]
    public void WholeDisksAreTheDiskEntriesWithoutASlice()
        => Assert.Equal(new[] { "disk2", "disk10" }, MacDiskInventory.WholeDisks(new[] { "disk10", "disk2s1", "disk2", "null", "rdisk2", "disk10s3" }));

    [Fact]
    public void ABlankDiskIsNamedByItsModel()
    {
        var inventory = new MacDiskInventory(() => new List<MacDiskDescription> { Blank(), Blank("disk0") with { Content = "GUID_partition_scheme" } }, NullLogger.Instance);

        var disk = Assert.Single(inventory.GetUnformatted());

        Assert.Equal("disk20", disk.Device);
        Assert.Equal("Samsung SSD 870 EVO", disk.Model);
        Assert.Equal(2000, disk.SizeBytes);
    }

    [Fact]
    public void WithNoModelTheMediaNameIsUsedWithoutItsSuffix()
    {
        var inventory = new MacDiskInventory(() => new List<MacDiskDescription> { Blank() with { Model = null, Name = "Disk Image Media" } }, NullLogger.Instance);

        Assert.Equal("Disk Image", Assert.Single(inventory.GetUnformatted()).Model);
    }

    [Fact]
    public void OnAMacTheSystemDiskIsReadAndIsNotBlank()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var inventory = new MacDiskInventory(NullLogger<MacDiskInventory>.Instance);
        var unformatted = inventory.GetUnformatted();

        Assert.DoesNotContain(unformatted, d => d.Device == "disk0");
        Assert.All(unformatted, d => Assert.True(d.SizeBytes > 0));
    }

    [Theory]
    [InlineData("USB", false, true)]
    [InlineData("Thunderbolt", false, true)]
    [InlineData("Apple Fabric", true, false)]
    [InlineData("Virtual Interface", false, false)]
    [InlineData(null, false, false)]
    public void AnExternalDiskIsOneMacOSMarksNotInternal(string? protocol, bool isInternal, bool external)
        => Assert.Equal(external, MacDiskInventory.IsExternal(protocol, isInternal));
}
