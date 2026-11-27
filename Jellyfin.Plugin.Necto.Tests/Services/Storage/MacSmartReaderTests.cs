using System;
using Jellyfin.Plugin.Necto.Configuration.Storage;
using Jellyfin.Plugin.Necto.Services.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

using Jellyfin.Plugin.Necto.Tests.Fakes.Storage;

namespace Jellyfin.Plugin.Necto.Tests.Services.Storage;

/// <summary>
/// On a Mac with an NVMe system disk, reads its SMART data for real through Apple's NVMe SMART plugin.
/// </summary>
public class MacSmartReaderTests
{
    private readonly ITestOutputHelper _output;

    public MacSmartReaderTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void OnAMacTheSystemDiskReportsItsHealth()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var reading = new MacSmartReader(NullLogger<MacSmartReader>.Instance).Read("/");
        _output.WriteLine(reading?.ToString() ?? "no reading");

        Assert.NotNull(reading);
        Assert.NotEqual(DiskHealth.Unknown, reading!.Health);
        Assert.InRange(reading.TemperatureCelsius ?? 0, 1, 100);
    }

    [Fact]
    public void AFolderWithNoDiskBehindItHasNoReading()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        Assert.Null(new MacSmartReader(NullLogger<MacSmartReader>.Instance).Read("/no/such/folder"));
    }
}
