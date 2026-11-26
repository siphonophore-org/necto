using System;
using System.Buffers.Binary;
using System.Globalization;
using Jellyfin.Plugin.Necto.Configuration.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// Reads SMART data on Windows from C#: the volume behind a drive letter names its physical disk, which is asked for
/// its NVMe health log, or for Windows' failure prediction when it is not NVMe. Reading a disk needs administrator
/// rights, which a Jellyfin service running as LocalSystem has.
/// </summary>
public sealed class WindowsSmartReader : ISmartReader
{
    private const uint IoctlStorageGetDeviceNumber = 0x002D1080;
    private const uint IoctlStoragePredictFailure = 0x002D1100;

    // STORAGE_PROPERTY_QUERY with StorageDeviceProtocolSpecificProperty, then STORAGE_PROTOCOL_SPECIFIC_DATA asking an
    // NVMe disk for log page 2, its SMART / Health Information log.
    private const int ProtocolSpecificProperty = 50;
    private const int ProtocolTypeNvme = 3;
    private const int NvmeDataTypeLogPage = 2;
    private const int SmartLogPage = 2;
    private const int ProtocolDataSize = 40;

    private readonly ILogger<WindowsSmartReader> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsSmartReader"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public WindowsSmartReader(ILogger<WindowsSmartReader> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public SmartReading? Read(string mountPoint)
    {
        if (!OperatingSystem.IsWindows() || !TryDriveLetter(mountPoint, out var letter))
        {
            return null;
        }

        var number = DiskNumberOf(letter);
        if (number is null)
        {
            return null;
        }

        using var disk = Open(@"\\.\PhysicalDrive" + number.Value.ToString(CultureInfo.InvariantCulture), WindowsNativeMethods.GenericRead);
        if (disk.IsInvalid)
        {
            _logger.LogDebug("Could not open disk {Number} for SMART data, which needs administrator rights", number);
            return null;
        }

        return ReadNvmeLog(disk) ?? ReadPrediction(disk);
    }

    /// <summary>
    /// Builds the request for an NVMe disk's SMART / Health Information log.
    /// </summary>
    /// <returns>The request, with room for the log after it.</returns>
    public static byte[] NvmeLogRequest()
    {
        var request = new byte[8 + ProtocolDataSize + NvmeSmartLog.Size];
        BinaryPrimitives.WriteInt32LittleEndian(request, ProtocolSpecificProperty);
        var data = request.AsSpan(8);
        BinaryPrimitives.WriteInt32LittleEndian(data, ProtocolTypeNvme);
        BinaryPrimitives.WriteInt32LittleEndian(data[4..], NvmeDataTypeLogPage);
        BinaryPrimitives.WriteInt32LittleEndian(data[8..], SmartLogPage);
        BinaryPrimitives.WriteInt32LittleEndian(data[16..], ProtocolDataSize);
        BinaryPrimitives.WriteInt32LittleEndian(data[20..], NvmeSmartLog.Size);
        return request;
    }

    /// <summary>
    /// Reads the log out of the reply to <see cref="NvmeLogRequest"/>: a version and size, the protocol data, then the
    /// log at the offset the protocol data gives.
    /// </summary>
    /// <param name="reply">What the disk returned.</param>
    /// <returns>The reading, or <c>null</c> when the reply holds no log.</returns>
    public static SmartReading? ParseNvmeReply(ReadOnlySpan<byte> reply)
    {
        if (reply.Length < 8 + ProtocolDataSize)
        {
            return null;
        }

        var offset = BinaryPrimitives.ReadInt32LittleEndian(reply[(8 + 16)..]);
        var length = BinaryPrimitives.ReadInt32LittleEndian(reply[(8 + 20)..]);
        var start = 8 + offset;
        return offset <= 0 || length < NvmeSmartLog.Size || start + NvmeSmartLog.Size > reply.Length
            ? null
            : NvmeSmartLog.Parse(reply.Slice(start, NvmeSmartLog.Size));
    }

    // Only a drive letter root has a volume to ask, such as C:\.
    private static bool TryDriveLetter(string mountPoint, out char letter)
    {
        letter = mountPoint.Length >= 2 && char.IsAsciiLetter(mountPoint[0]) && mountPoint[1] == ':' ? char.ToUpperInvariant(mountPoint[0]) : '\0';
        return letter != '\0' && mountPoint.Length <= 3;
    }

    private static int? DiskNumberOf(char letter)
    {
        using var volume = Open(@"\\.\" + letter + ":", 0);
        if (volume.IsInvalid)
        {
            return null;
        }

        // STORAGE_DEVICE_NUMBER: the device type, then the disk number.
        var output = new byte[12];
        return WindowsNativeMethods.DeviceIoControl(volume, IoctlStorageGetDeviceNumber, null, 0, output, output.Length, out var returned, IntPtr.Zero) && returned >= 8
            ? BinaryPrimitives.ReadInt32LittleEndian(output.AsSpan(4))
            : null;
    }

    private static SmartReading? ReadNvmeLog(SafeFileHandle disk)
    {
        var request = NvmeLogRequest();
        var reply = new byte[request.Length];
        return WindowsNativeMethods.DeviceIoControl(disk, WindowsNativeMethods.IoctlStorageQueryProperty, request, request.Length, reply, reply.Length, out var returned, IntPtr.Zero)
            ? ParseNvmeReply(reply.AsSpan(0, returned))
            : null;
    }

    // STORAGE_PREDICT_FAILURE: a nonzero first field means the disk predicts it will fail.
    private static SmartReading? ReadPrediction(SafeFileHandle disk)
    {
        var output = new byte[516];
        return WindowsNativeMethods.DeviceIoControl(disk, IoctlStoragePredictFailure, null, 0, output, output.Length, out var returned, IntPtr.Zero) && returned >= 4
            ? new SmartReading(BinaryPrimitives.ReadInt32LittleEndian(output) == 0 ? DiskHealth.Good : DiskHealth.Failing)
            : null;
    }

    private static SafeFileHandle Open(string path, uint access)
        => WindowsNativeMethods.CreateFileW(path, access, WindowsNativeMethods.ShareReadWrite, IntPtr.Zero, WindowsNativeMethods.OpenExisting, 0, IntPtr.Zero);
}
