using System;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Configuration.Storage;
using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// Mounts a disk or share again on Linux from C#: systemd is asked over the system D-Bus to start the mount unit
/// it keeps for each /etc/fstab entry, the way systemctl start does. Only disks in /etc/fstab can be mounted again,
/// and Jellyfin needs to run as root or be allowed by a polkit rule to start mount units.
/// </summary>
public sealed class LinuxRemounter : IRemounter
{
    private readonly ILogger<LinuxRemounter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LinuxRemounter"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public LinuxRemounter(ILogger<LinuxRemounter> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public bool TakesAddress => false;

    /// <inheritdoc />
    public string? SourceOf(string mountPoint) => null;

    /// <inheritdoc />
    public RemountResult Remount(string mountPoint, string? source)
    {
        var address = DBusAddress.System;
        if (string.IsNullOrEmpty(address))
        {
            return RemountResult.Unsupported;
        }

        var unit = SystemdUnit.MountUnit(MountPaths.Normalize(mountPoint));
        try
        {
            StartUnitAsync(address, unit).GetAwaiter().GetResult();
            return RemountResult.Success;
        }
        catch (DBusErrorReplyException ex)
        {
            _logger.LogDebug(ex, "systemd could not start {Unit}", unit);
            return Classify(ex.ErrorName);
        }
        catch (Exception ex) when (ex is DBusExceptionBase or System.IO.IOException or System.Net.Sockets.SocketException or InvalidOperationException)
        {
            _logger.LogDebug(ex, "Could not reach systemd to start {Unit}", unit);
            return RemountResult.Unsupported;
        }
    }

    /// <summary>
    /// Reads what an error from systemd means for a remount.
    /// </summary>
    /// <param name="errorName">The D-Bus error name.</param>
    /// <returns>Unsupported for a disk with no fstab entry, else why it failed.</returns>
    public static RemountResult Classify(string? errorName) => errorName switch
    {
        // A mount that is not in /etc/fstab has no unit once it is unmounted.
        "org.freedesktop.systemd1.NoSuchUnit" or "org.freedesktop.systemd1.LoadFailed" => RemountResult.Unsupported,
        "org.freedesktop.DBus.Error.AccessDenied" or "org.freedesktop.DBus.Error.InteractiveAuthorizationRequired" => RemountResult.Failed(RemountFailure.Denied),
        _ => RemountResult.Failed(RemountFailure.Other)
    };

    private static async Task StartUnitAsync(string address, string unit)
    {
        using var connection = new DBusConnection(address);
        await connection.ConnectAsync().ConfigureAwait(false);
        MessageBuffer message;
        using (var writer = connection.GetMessageWriter())
        {
            writer.WriteMethodCallHeader("org.freedesktop.systemd1", "/org/freedesktop/systemd1", "org.freedesktop.systemd1.Manager", "StartUnit", "ss", MessageFlags.None);
            writer.WriteString(unit);
            writer.WriteString("replace");
            message = writer.CreateMessage();
        }

        await connection.CallMethodAsync(message).ConfigureAwait(false);
    }
}
