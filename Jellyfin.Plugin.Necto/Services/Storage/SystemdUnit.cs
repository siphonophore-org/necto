using System;
using System.Globalization;
using System.Text;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// Names systemd's mount unit for a path, as systemd-escape --path --suffix=mount does. Pure, so it is testable.
/// </summary>
public static class SystemdUnit
{
    /// <summary>
    /// Names the mount unit for a mount point, such as mnt-media.mount for /mnt/media.
    /// </summary>
    /// <param name="mountPoint">An absolute path.</param>
    /// <returns>The unit name.</returns>
    public static string MountUnit(string mountPoint)
    {
        ArgumentNullException.ThrowIfNull(mountPoint);
        var parts = mountPoint.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return "-.mount";
        }

        var name = new StringBuilder();
        for (var i = 0; i < parts.Length; i++)
        {
            if (i > 0)
            {
                name.Append('-');
            }

            var bytes = Encoding.UTF8.GetBytes(parts[i]);
            for (var j = 0; j < bytes.Length; j++)
            {
                var b = bytes[j];
                var plain = (b >= 'a' && b <= 'z') || (b >= 'A' && b <= 'Z') || (b >= '0' && b <= '9') || b == ':' || b == '_' || (b == '.' && (i > 0 || j > 0));
                if (plain)
                {
                    name.Append((char)b);
                }
                else
                {
                    name.Append("\\x").Append(b.ToString("x2", CultureInfo.InvariantCulture));
                }
            }
        }

        return name.Append(".mount").ToString();
    }
}
