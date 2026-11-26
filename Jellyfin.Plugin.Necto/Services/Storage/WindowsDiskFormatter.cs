using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Jellyfin.Plugin.Necto.Configuration.Storage;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// Formats a disk on Windows from C# through the Storage Management API, the WMI classes Disk Management uses, reached
/// through Windows' own WMI scripting objects: the disk is brought online, given a GPT partition table and one
/// partition with the next free drive letter, and that volume is formatted. Windows mounts it again at every boot.
/// Needs administrator rights, which a Jellyfin service running as LocalSystem has.
/// </summary>
public sealed class WindowsDiskFormatter : IDiskFormatter
{
    private const string Namespace = @"root\Microsoft\Windows\Storage";
    private const ushort Gpt = 2;
    private const ushort Raw = 0;

    private readonly ILogger<WindowsDiskFormatter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsDiskFormatter"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public WindowsDiskFormatter(ILogger<WindowsDiskFormatter> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> FileSystems { get; } = new[] { "ntfs", "exfat" };

    /// <inheritdoc />
    public bool DashesForSpaces => false;

    /// <inheritdoc />
    public string? MountRoot => null;

    /// <summary>
    /// Reads the disk number out of the name the Storage tab lists, such as 2 from Disk 2.
    /// </summary>
    /// <param name="device">The listed name.</param>
    /// <returns>The number, or <c>null</c>.</returns>
    public static int? DiskNumber(string device)
        => device is not null && device.StartsWith("Disk ", StringComparison.Ordinal)
            && int.TryParse(device.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;

    /// <inheritdoc />
    public FormatResult Format(RawDisk disk, string name, string fileSystem)
    {
        ArgumentNullException.ThrowIfNull(disk);
        var number = DiskNumber(disk.Device);
        if (!OperatingSystem.IsWindows() || number is null || Array.IndexOf((string[])FileSystems, fileSystem) < 0)
        {
            return FormatResult.Failed("This disk can't be formatted");
        }

        try
        {
            return FormatOnWindows(number.Value, name, fileSystem == "ntfs" ? "NTFS" : "exFAT");
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            _logger.LogWarning(ex, "Windows could not format disk {Number}", number);
            return FormatResult.Failed(ex.Message);
        }
    }

    [SupportedOSPlatform("windows")]
    private static FormatResult FormatOnWindows(int number, string name, string fileSystem)
    {
        var locatorType = Type.GetTypeFromProgID("WbemScripting.SWbemLocator") ?? throw new InvalidOperationException("WMI is not available");
        dynamic locator = Activator.CreateInstance(locatorType)!;
        dynamic service = locator.ConnectServer(".", Namespace);

        dynamic disk = Single(service, "SELECT * FROM MSFT_Disk WHERE Number = " + number.ToString(CultureInfo.InvariantCulture))
            ?? throw new InvalidOperationException("Windows does not list disk " + number.ToString(CultureInfo.InvariantCulture));
        if ((bool)disk.IsOffline)
        {
            Call(disk, "Online");
        }

        if ((bool)disk.IsReadOnly)
        {
            Call(disk, "SetAttributes", ("IsReadOnly", false));
        }

        if (Convert.ToUInt16(disk.PartitionStyle, CultureInfo.InvariantCulture) == Raw)
        {
            Call(disk, "Initialize", ("PartitionStyle", Gpt));
        }

        dynamic created = Call(disk, "CreatePartition", ("UseMaximumSize", true), ("AssignDriveLetter", true));
        var partitionNumber = Convert.ToUInt32(created.Properties_.Item("CreatedPartition").Value.PartitionNumber, CultureInfo.InvariantCulture);
        var query = "SELECT * FROM MSFT_Partition WHERE DiskNumber = " + number.ToString(CultureInfo.InvariantCulture) + " AND PartitionNumber = " + partitionNumber.ToString(CultureInfo.InvariantCulture);
        dynamic partition = Single(service, query) ?? throw new InvalidOperationException("The new partition is missing");

        dynamic volume = null!;
        foreach (dynamic found in service.ExecQuery("ASSOCIATORS OF {" + (string)partition.Path_.RelPath + "} WHERE AssocClass = MSFT_PartitionToVolume"))
        {
            volume = found;
        }

        Call(volume ?? throw new InvalidOperationException("The new partition has no volume"), "Format", ("FileSystem", fileSystem), ("FileSystemLabel", name), ("Full", false), ("Force", true));

        // The drive letter is set once the partition is read again.
        // The format has worked by now, so a partition that can't be read again only leaves the letter unknown, and the
        // new drive is found among the mounted ones instead.
        partition = Single(service, query);
        if (partition is null)
        {
            return new FormatResult(true);
        }

        var letter = Convert.ToChar(Convert.ToUInt16(partition.DriveLetter, CultureInfo.InvariantCulture));
        return new FormatResult(true, char.IsAsciiLetter(letter) ? letter + @":\" : null);
    }

    [SupportedOSPlatform("windows")]
    private static dynamic? Single(dynamic service, string query)
    {
        foreach (dynamic found in service.ExecQuery(query))
        {
            return found;
        }

        return null;
    }

    // Runs a WMI method and fails on any return value but 0.
    [SupportedOSPlatform("windows")]
    private static dynamic Call(dynamic target, string method, params (string Name, object Value)[] arguments)
    {
        dynamic? input = null;
        if (arguments.Length > 0)
        {
            input = target.Methods_.Item(method).InParameters.SpawnInstance_();
            foreach (var (argumentName, value) in arguments)
            {
                input.Properties_.Item(argumentName).Value = value;
            }
        }

        dynamic output = input is null ? target.ExecMethod_(method) : target.ExecMethod_(method, input);
        var code = Convert.ToUInt32(output.Properties_.Item("ReturnValue").Value, CultureInfo.InvariantCulture);
        if (code != 0)
        {
            throw new InvalidOperationException(method + " returned " + code.ToString(CultureInfo.InvariantCulture));
        }

        return output;
    }
}
