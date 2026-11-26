using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.Necto.Services.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

using Jellyfin.Plugin.Necto.Tests.Fakes.Storage;

namespace Jellyfin.Plugin.Necto.Tests.Services.Storage;

/// <summary>
/// On a Mac, attaches a blank disk image with no partition map, checks the inventory lists it as unformatted, formats
/// it with diskutil, and detaches it. Runs only with STORAGE_LIVE_MAC=1, since it creates and erases a disk image.
/// </summary>
public class MacLiveFormatTests
{
    private readonly ITestOutputHelper _output;

    public MacLiveFormatTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void ABlankDiskImageIsFormattedAndMountedInVolumes()
    {
        if (!OperatingSystem.IsMacOS() || Environment.GetEnvironmentVariable("STORAGE_LIVE_MAC") != "1")
        {
            return;
        }

        var image = Path.Combine(Path.GetTempPath(), "storage-live-" + Guid.NewGuid().ToString("N") + ".img");
        using (var file = File.Create(image))
        {
            file.SetLength(256L * 1024 * 1024);
        }

        // A raw image of zeros attaches as a whole disk with nothing on it, as a new disk would.
        var attached = Output("/usr/bin/hdiutil", "attach -nomount -imagekey diskimage-class=CRawDiskImage " + image).Trim();
        var device = attached.Split(' ', '\t')[0].Replace("/dev/", string.Empty, StringComparison.Ordinal);
        _output.WriteLine("Attached " + device);
        try
        {
            Assert.Matches("^disk[0-9]+$", device);
            var disk = new MacDiskInventory(NullLogger<MacDiskInventory>.Instance).GetUnformatted().SingleOrDefault(d => d.Device == device);
            Assert.NotNull(disk);

            var result = new MacDiskFormatter(NullLogger<MacDiskFormatter>.Instance).Format(disk!, "LiveTest", "exfat");
            _output.WriteLine(result.ToString());

            Assert.True(result.Formatted, result.Error);
            Assert.True(Directory.Exists("/Volumes/LiveTest"));
            Assert.DoesNotContain(new MacDiskInventory(NullLogger<MacDiskInventory>.Instance).GetUnformatted(), d => d.Device == device);
        }
        finally
        {
            Output("/usr/bin/hdiutil", "detach -force /dev/" + device);
            File.Delete(image);
        }
    }

    private static string Output(string file, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(file, arguments) { RedirectStandardOutput = true, RedirectStandardError = true });
        var text = process!.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return text;
    }
}
