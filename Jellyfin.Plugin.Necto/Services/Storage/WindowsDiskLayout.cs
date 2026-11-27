using System;
using System.Buffers.Binary;
using System.Linq;
using System.Text;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// Reads the structures Windows returns for a physical disk: its partition layout and its storage descriptor. Pure, so
/// the byte layouts are testable on any OS.
/// </summary>
public static class WindowsDiskLayout
{
    /// <summary>The partition style of a disk that was never initialized.</summary>
    public const int RawStyle = 2;

    // DRIVE_LAYOUT_INFORMATION_EX: style and count, a 40 byte MBR or GPT union, then one 144 byte entry per partition.
    private const int EntriesOffset = 48;
    private const int EntrySize = 144;
    private const int MbrStyle = 0;
    private const int GptStyle = 1;

    // The hidden partition Windows adds when it initializes a disk as GPT, which holds no file system.
    private static readonly Guid MicrosoftReserved = new("e3c9e316-0b5c-4db8-817d-f92df00215ae");

    /// <summary>
    /// Tells whether a disk's partition layout holds nothing: never initialized, or initialized with no partition
    /// besides the Microsoft Reserved one.
    /// </summary>
    /// <param name="layout">What IOCTL_DISK_GET_DRIVE_LAYOUT_EX returned.</param>
    /// <returns><c>true</c> for an empty disk, <c>false</c> for one with partitions, or <c>null</c> when the layout cannot be read.</returns>
    public static bool? IsEmpty(ReadOnlySpan<byte> layout)
    {
        if (layout.Length < 8)
        {
            return null;
        }

        var style = BinaryPrimitives.ReadInt32LittleEndian(layout);
        var count = BinaryPrimitives.ReadInt32LittleEndian(layout[4..]);
        if (style == RawStyle)
        {
            return true;
        }

        if (style != MbrStyle && style != GptStyle)
        {
            return null;
        }

        for (var i = 0; i < count; i++)
        {
            var start = EntriesOffset + (i * EntrySize);
            if (start + EntrySize > layout.Length)
            {
                return null;
            }

            var entry = layout.Slice(start, EntrySize);
            if (style == MbrStyle ? UsedMbr(entry) : UsedGpt(entry))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Reads a disk's model from its storage descriptor: the vendor and product, without padding.
    /// </summary>
    /// <param name="descriptor">What IOCTL_STORAGE_QUERY_PROPERTY returned for the device property.</param>
    /// <returns>The model, or <c>null</c> when the descriptor names none.</returns>
    public static string? Model(ReadOnlySpan<byte> descriptor)
    {
        if (descriptor.Length < 20)
        {
            return null;
        }

        var vendor = AnsiAt(descriptor, BinaryPrimitives.ReadInt32LittleEndian(descriptor[12..]));
        var product = AnsiAt(descriptor, BinaryPrimitives.ReadInt32LittleEndian(descriptor[16..]));
        var model = string.Join(' ', new[] { vendor, product }.Where(part => !string.IsNullOrEmpty(part)));
        return model.Length == 0 ? null : model;
    }

    /// <summary>
    /// Tells from its storage descriptor whether a disk is plugged in from outside: on USB, FireWire, or an SD card
    /// reader, or marked as removable media.
    /// </summary>
    /// <param name="descriptor">What IOCTL_STORAGE_QUERY_PROPERTY returned for the device property.</param>
    /// <returns><c>true</c> for an external disk.</returns>
    public static bool IsExternal(ReadOnlySpan<byte> descriptor)
    {
        if (descriptor.Length < 32)
        {
            return false;
        }

        // STORAGE_DEVICE_DESCRIPTOR: RemovableMedia at byte 10, BusType at byte 28. BusType1394 is 4, BusTypeUsb 7,
        // and BusTypeSd 12.
        var bus = BinaryPrimitives.ReadInt32LittleEndian(descriptor[28..]);
        return descriptor[10] != 0 || bus is 4 or 7 or 12;
    }

    // An MBR slot is used when its partition type is not zero. Windows always reports four slots.
    private static bool UsedMbr(ReadOnlySpan<byte> entry) => entry[32] != 0;

    // A GPT entry is used unless its type is empty or the Microsoft Reserved partition.
    private static bool UsedGpt(ReadOnlySpan<byte> entry)
    {
        var type = new Guid(entry.Slice(32, 16));
        return type != Guid.Empty && type != MicrosoftReserved;
    }

    private static string? AnsiAt(ReadOnlySpan<byte> buffer, int offset)
    {
        if (offset <= 0 || offset >= buffer.Length)
        {
            return null;
        }

        var text = buffer[offset..];
        var end = text.IndexOf((byte)0);
        var value = Encoding.ASCII.GetString(end < 0 ? text : text[..end]).Trim();
        return value.Length == 0 ? null : value;
    }
}
