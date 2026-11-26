using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Necto.Configuration.Storage;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// Formats a disk on macOS with Apple's diskutil, the one place the plugin runs an outside tool: macOS has no public
/// API for formatting, and diskutil is what Disk Utility itself uses. The disk gets a GPT partition map and one APFS
/// or exFAT volume, which macOS mounts in /Volumes under its name and mounts again whenever the disk is plugged in.
/// </summary>
public sealed partial class MacDiskFormatter : IDiskFormatter
{
    private const string DiskUtil = "/usr/sbin/diskutil";

    // The file systems offered, as Disk Utility names them, and diskutil's name for each.
    private static readonly Dictionary<string, string> Personalities = new(StringComparer.Ordinal)
    {
        ["apfs"] = "APFS",
        ["exfat"] = "ExFAT"
    };

    private readonly ILogger<MacDiskFormatter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MacDiskFormatter"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public MacDiskFormatter(ILogger<MacDiskFormatter> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    // HFS+ is left out: it only helps a Mac too old for Jellyfin.
    public IReadOnlyList<string> FileSystems { get; } = new[] { "apfs", "exfat" };

    /// <inheritdoc />
    public bool DashesForSpaces => false;

    /// <inheritdoc />
    public string? MountRoot => "/Volumes";

    /// <summary>
    /// Builds diskutil's arguments to erase a whole disk with one volume.
    /// </summary>
    /// <param name="device">The disk's BSD name, such as disk4.</param>
    /// <param name="name">The volume name.</param>
    /// <param name="fileSystem">One of <see cref="FileSystems"/>.</param>
    /// <returns>The arguments, or <c>null</c> when the device or file system is not one diskutil should be given.</returns>
    public static IReadOnlyList<string>? Arguments(string device, string name, string fileSystem)
    {
        // Only a whole disk, never a volume or a path.
        if (!WholeDisk().IsMatch(device ?? string.Empty) || !Personalities.TryGetValue(fileSystem ?? string.Empty, out var personality))
        {
            return null;
        }

        return new[] { "eraseDisk", personality, name, "GPT", device! };
    }

    /// <inheritdoc />
    public FormatResult Format(RawDisk disk, string name, string fileSystem)
    {
        ArgumentNullException.ThrowIfNull(disk);
        var arguments = Arguments(disk.Device, name, fileSystem);
        if (!OperatingSystem.IsMacOS() || arguments is null)
        {
            return FormatResult.Failed("This disk cannot be formatted");
        }

        var start = new ProcessStartInfo(DiskUtil) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start);
        if (process is null)
        {
            return FormatResult.Failed("diskutil could not start");
        }

        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode == 0)
        {
            return new FormatResult(true);
        }

        var message = string.IsNullOrWhiteSpace(error) ? output.GetAwaiter().GetResult() : error;
        _logger.LogWarning("diskutil could not format {Device}: {Message}", disk.Device, message.Trim());
        return FormatResult.Failed(message.Trim());
    }

    [GeneratedRegex("^disk[0-9]+$")]
    private static partial Regex WholeDisk();
}
