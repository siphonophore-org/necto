using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Configuration.Storage;
using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// Formats a disk on Linux from C# through udisks2 over the system D-Bus, the way GNOME Disks does: a GPT partition
/// table, one partition with the file system owned by Jellyfin's user, an /etc/fstab entry so it mounts at every boot
/// and can be remounted, then a mount. Jellyfin needs to run as root or be allowed by a polkit rule to manage disks.
/// </summary>
public sealed class LinuxDiskFormatter : IDiskFormatter
{
    private const string Service = "org.freedesktop.UDisks2";
    private const string ManagerPath = "/org/freedesktop/UDisks2/Manager";

    // The file systems offered, when udisks has the tools to make them.
    private static readonly string[] Candidates = { "ext4", "xfs", "btrfs", "exfat", "ntfs" };

    private readonly ILogger<LinuxDiskFormatter> _logger;
    private List<string>? _fileSystems;

    /// <summary>
    /// Initializes a new instance of the <see cref="LinuxDiskFormatter"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public LinuxDiskFormatter(ILogger<LinuxDiskFormatter> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> FileSystems
    {
        get
        {
            // Asked once udisks answers, so a server that starts before udisks asks again later.
            var known = _fileSystems;
            if (known is null || known.Count == 0)
            {
                known = AvailableAsync().GetAwaiter().GetResult();
                _fileSystems = known;
            }

            return known;
        }
    }

    // Writes a method's arguments. The writer is a struct, so it is passed by reference: a copy would leave the
    // message's length unset, and the bus would drop the connection.
    private delegate void WriteBody(ref MessageWriter writer);

    /// <inheritdoc />
    public bool DashesForSpaces => true;

    /// <inheritdoc />
    public string? MountRoot => "/mnt";

    /// <inheritdoc />
    public FormatResult Format(RawDisk disk, string name, string fileSystem)
    {
        ArgumentNullException.ThrowIfNull(disk);
        var address = DBusAddress.System;
        if (string.IsNullOrEmpty(address))
        {
            return FormatResult.Failed("udisks is not running");
        }

        try
        {
            return FormatAsync(address, disk.Device, name, fileSystem).GetAwaiter().GetResult();
        }
        catch (DBusErrorReplyException ex)
        {
            _logger.LogWarning(ex, "udisks could not format {Device}", disk.Device);
            return FormatResult.Failed(Describe(ex));
        }
        catch (Exception ex) when (ex is DBusExceptionBase or System.Net.Sockets.SocketException)
        {
            _logger.LogWarning(ex, "Could not reach udisks to format {Device}", disk.Device);
            return FormatResult.Failed("udisks is not running");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            // Anything else is told back as it happened, so the dashboard can show it and nothing is left half said.
            _logger.LogWarning(ex, "Could not format {Device}", disk.Device);
            return FormatResult.Failed(ex.Message);
        }
    }

    /// <summary>
    /// Builds the /etc/fstab entry for a new file system: by UUID, never failing a boot when the disk is gone, and
    /// creating its folder when it mounts. exFAT and NTFS keep no Linux owners, so their entry names Jellyfin's user
    /// and group, or whoever mounts them, root at boot, would own every file and Jellyfin could save no metadata.
    /// </summary>
    /// <param name="uuid">The file system's UUID.</param>
    /// <param name="mountPoint">Where it mounts.</param>
    /// <param name="fileSystem">The file system type.</param>
    /// <param name="uid">Jellyfin's user id.</param>
    /// <param name="gid">Jellyfin's group id.</param>
    /// <returns>The entry's fields, as udisks takes them.</returns>
    public static (string FsName, string Dir, string Type, string Options, int Pass) FstabEntry(string uuid, string mountPoint, string fileSystem, uint uid, uint gid)
    {
        var ownerless = fileSystem is "exfat" or "ntfs";
        var options = "defaults,nofail,x-mount.mkdir" + (ownerless ? string.Create(CultureInfo.InvariantCulture, $",uid={uid},gid={gid},umask=002") : string.Empty);
        return ("UUID=" + uuid, mountPoint, fileSystem, options, ownerless || fileSystem == "btrfs" ? 0 : 2);
    }

    // Says what went wrong in words an admin can act on.
    private static string Describe(DBusErrorReplyException ex) => ex.ErrorName switch
    {
        "org.freedesktop.UDisks2.Error.NotAuthorized" or "org.freedesktop.UDisks2.Error.NotAuthorizedCanObtain" or "org.freedesktop.DBus.Error.AccessDenied"
            => "Jellyfin isn't allowed to manage disks",
        _ => ex.Message
    };

    private static async Task<FormatResult> FormatAsync(string address, string device, string name, string fileSystem)
    {
        using var connection = new DBusConnection(address);
        await connection.ConnectAsync().ConfigureAwait(false);

        var objects = await LinuxSmartReader.GetManagedObjectsAsync(address).ConfigureAwait(false);
        var disk = FindBlock(objects, device) ?? throw new InvalidOperationException("udisks does not know " + device);

        // udisks says for itself whether anything on the disk is mounted or in use as swap, before anything changes, so
        // a busy disk keeps its /etc/fstab entries.
        var blocks = new[] { disk }.Concat(PartitionsOf(objects, disk)).ToList();
        if (blocks.Any(b => InUse(objects, b)))
        {
            throw new InvalidOperationException("Something on " + device + " is mounted or in use");
        }

        // A disk being erased loses its /etc/fstab entries too, so the new file system's entry is the only one left
        // for its folder and no entry points at a file system that is gone.
        foreach (var stale in blocks)
        {
            await RemoveFstabEntriesAsync(connection, stale).ConfigureAwait(false);
        }

        // A GPT partition table, then one partition over the whole disk with the file system, owned by Jellyfin's user.
        await CallAsync(connection, disk, "org.freedesktop.UDisks2.Block", "Format", "sa{sv}", (ref MessageWriter w) =>
        {
            w.WriteString("gpt");
            w.WriteDictionary(new Dictionary<string, VariantValue>());
        }).ConfigureAwait(false);
        var partition = await CallAsync(connection, disk, "org.freedesktop.UDisks2.PartitionTable", "CreatePartitionAndFormat", "ttssa{sv}sa{sv}", (ref MessageWriter w) =>
        {
            w.WriteUInt64(0);
            w.WriteUInt64(0);
            w.WriteString(string.Empty);
            w.WriteString(name);
            w.WriteDictionary(new Dictionary<string, VariantValue>());
            w.WriteString(fileSystem);
            // exFAT and NTFS have no Linux owners, so only the others are given to Jellyfin's user.
            var format = new Dictionary<string, VariantValue> { ["label"] = VariantValue.String(name) };
            if (fileSystem is not ("exfat" or "ntfs"))
            {
                format["take-ownership"] = VariantValue.Bool(true);
            }

            w.WriteDictionary(format);
        }, r => r.ReadObjectPathAsString()).ConfigureAwait(false);

        objects = await LinuxSmartReader.GetManagedObjectsAsync(address).ConfigureAwait(false);
        var uuid = objects.TryGetValue(partition, out var created) && created.TryGetValue("org.freedesktop.UDisks2.Block", out var block)
            && block.TryGetValue("IdUUID", out var id) && id is string text && text.Length > 0
            ? text
            : throw new InvalidOperationException("The new partition has no UUID");

        var mountPoint = FormatNames.MountPoint("/mnt", name);
        var entry = FstabEntry(uuid, mountPoint, fileSystem, UnixNative.getuid(), UnixNative.getgid());
        await CallAsync(connection, partition, "org.freedesktop.UDisks2.Block", "AddConfigurationItem", "(sa{sv})a{sv}", (ref MessageWriter w) =>
        {
            w.WriteStructureStart();
            w.WriteString("fstab");
            w.WriteDictionary(new Dictionary<string, VariantValue>
            {
                ["fsname"] = Bytes(entry.FsName),
                ["dir"] = Bytes(entry.Dir),
                ["type"] = Bytes(entry.Type),
                ["opts"] = Bytes(entry.Options),
                ["freq"] = VariantValue.Int32(0),
                ["passno"] = VariantValue.Int32(entry.Pass)
            });
            w.WriteDictionary(new Dictionary<string, VariantValue>());
        }).ConfigureAwait(false);

        // systemd reads /etc/fstab again, so the new disk has a mount unit to remount it by.
        try
        {
            await CallAsync(connection, "/org/freedesktop/systemd1", "org.freedesktop.systemd1.Manager", "Reload", null, null, "org.freedesktop.systemd1").ConfigureAwait(false);
        }
        catch (DBusErrorReplyException)
        {
            // The entry still mounts at the next boot, and udisks mounts it now.
        }

        // The entry's x-mount.mkdir makes the folder as it mounts, with udisks' rights, since Jellyfin's own user may
        // not write to /mnt.
        var mounted = await CallAsync(connection, partition, "org.freedesktop.UDisks2.Filesystem", "Mount", "a{sv}", (ref MessageWriter w) => w.WriteDictionary(new Dictionary<string, VariantValue>()), r => r.ReadString()).ConfigureAwait(false);
        return new FormatResult(true, mounted);
    }

    // Whether udisks sees a block device mounted or in use as swap.
    private static bool InUse(Dictionary<string, Dictionary<string, Dictionary<string, object?>>> objects, string block)
    {
        if (!objects.TryGetValue(block, out var interfaces))
        {
            return false;
        }

        var mounted = interfaces.TryGetValue("org.freedesktop.UDisks2.Filesystem", out var fs)
            && fs.TryGetValue("MountPoints", out var points) && points is System.Collections.ICollection { Count: > 0 };
        var swapping = interfaces.TryGetValue("org.freedesktop.UDisks2.Swapspace", out var swap)
            && swap.TryGetValue("Active", out var active) && active is true;
        return mounted || swapping;
    }

    // The partitions udisks lists on a disk.
    private static IEnumerable<string> PartitionsOf(IReadOnlyDictionary<string, Dictionary<string, Dictionary<string, object?>>> objects, string disk)
        => objects.Where(o => o.Value.TryGetValue("org.freedesktop.UDisks2.Partition", out var partition)
            && partition.TryGetValue("Table", out var table) && table as string == disk).Select(o => o.Key);

    // Removes every /etc/fstab entry udisks ties to a block device, through udisks, which matches them by UUID or path.
    private static async Task RemoveFstabEntriesAsync(DBusConnection connection, string block)
    {
        var configuration = await CallAsync(connection, block, "org.freedesktop.DBus.Properties", "Get", "ss", (ref MessageWriter w) =>
        {
            w.WriteString("org.freedesktop.UDisks2.Block");
            w.WriteString("Configuration");
        }, r => r.ReadVariantValue()).ConfigureAwait(false);

        // The property is a(sa{sv}): each entry's kind, fstab or crypttab, and its fields.
        var items = configuration.Type == VariantValueType.Variant ? configuration.GetVariantValue() : configuration;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items.GetItem(i);
            if (item.GetItem(0).GetString() != "fstab")
            {
                continue;
            }

            var fields = new Dictionary<string, VariantValue>(StringComparer.Ordinal);
            var details = item.GetItem(1);
            for (var j = 0; j < details.Count; j++)
            {
                var entry = details.GetDictionaryEntry(j);
                var value = entry.Value.Type == VariantValueType.Variant ? entry.Value.GetVariantValue() : entry.Value;
                fields[entry.Key.GetString()] = value;
            }

            // An entry that names the disk by its label may be another disk's with the same label, so it is kept.
            var fsname = fields.TryGetValue("fsname", out var name) && name.Type == VariantValueType.Array ? Encoding.UTF8.GetString(name.GetArray<byte>()).TrimEnd('\0') : string.Empty;
            if (!fsname.StartsWith("UUID=", StringComparison.Ordinal) && !fsname.StartsWith("PARTUUID=", StringComparison.Ordinal) && !fsname.StartsWith("/dev/", StringComparison.Ordinal))
            {
                continue;
            }

            await CallAsync(connection, block, "org.freedesktop.UDisks2.Block", "RemoveConfigurationItem", "(sa{sv})a{sv}", (ref MessageWriter w) =>
            {
                w.WriteStructureStart();
                w.WriteString("fstab");
                w.WriteDictionary(fields);
                w.WriteDictionary(new Dictionary<string, VariantValue>());
            }).ConfigureAwait(false);
        }
    }

    // The block device udisks keeps for a device node, such as /dev/sdb.
    private static string? FindBlock(IReadOnlyDictionary<string, Dictionary<string, Dictionary<string, object?>>> objects, string device)
        => objects.FirstOrDefault(o => o.Value.TryGetValue("org.freedesktop.UDisks2.Block", out var block)
            && (Text(block, "Device") == device || Text(block, "PreferredDevice") == device)).Key;

    private static string? Text(Dictionary<string, object?> map, string key)
    {
        if (!map.TryGetValue(key, out var value) || value is not byte[] bytes)
        {
            return null;
        }

        var end = Array.IndexOf(bytes, (byte)0);
        return Encoding.UTF8.GetString(bytes, 0, end < 0 ? bytes.Length : end);
    }

    // udisks takes /etc/fstab's fields as NUL terminated byte strings.
    private static VariantValue Bytes(string text) => VariantValue.Array(Encoding.UTF8.GetBytes(text + "\0"));

    private static Task<bool> CallAsync(DBusConnection connection, string path, string iface, string method, string? signature, WriteBody? write, string service = Service)
        => CallAsync<bool>(connection, path, iface, method, signature, write, _ => true, service);

    private static async Task<T> CallAsync<T>(DBusConnection connection, string path, string iface, string method, string? signature, WriteBody? write, Func<Reader, T> read, string service = Service)
    {
        MessageBuffer message;
        var writer = connection.GetMessageWriter();
        try
        {
            writer.WriteMethodCallHeader(service, path, iface, method, signature, MessageFlags.None);
            write?.Invoke(ref writer);
            message = writer.CreateMessage();
        }
        finally
        {
            writer.Dispose();
        }

        return await connection.CallMethodAsync(message, (Message reply, object? _) =>
        {
            var reader = reply.GetBodyReader();
            return read(reader);
        }, null).ConfigureAwait(false);
    }

    // Asks udisks which file systems it has the tools to make, through Manager.CanFormat.
    private async Task<List<string>> AvailableAsync()
    {
        var address = DBusAddress.System;
        var found = new List<string>();
        if (string.IsNullOrEmpty(address))
        {
            return found;
        }

        try
        {
            using var connection = new DBusConnection(address);
            await connection.ConnectAsync().ConfigureAwait(false);
            foreach (var type in Candidates)
            {
                var can = await CallAsync(connection, ManagerPath, "org.freedesktop.UDisks2.Manager", "CanFormat", "s", (ref MessageWriter w) => w.WriteString(type), r =>
                {
                    r.AlignStruct();
                    return r.ReadBool();
                }).ConfigureAwait(false);
                if (can)
                {
                    found.Add(type);
                }
            }
        }
        catch (Exception ex) when (ex is DBusExceptionBase or IOException or System.Net.Sockets.SocketException or InvalidOperationException)
        {
            _logger.LogDebug(ex, "Could not ask udisks which file systems it can make");
        }

        return found;
    }
}
