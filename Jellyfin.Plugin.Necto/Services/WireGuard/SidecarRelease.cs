using System;
using System.Runtime.InteropServices;

namespace Jellyfin.Plugin.Necto.Services.WireGuard;

/// <summary>
/// Names the wgsidecar release Necto downloads, built from <c>sidecar/</c> on wireguard-go by the Release wgsidecar
/// workflow, and the file in it for each platform.
/// </summary>
public static class SidecarRelease
{
    /// <summary>The release Necto downloads. Raise it with each sidecar change, then run the workflow.</summary>
    public const string Version = "1.0.0";

    /// <summary>The repository whose releases hold the sidecar.</summary>
    public const string Repository = "siphonophore-org/necto";

    /// <summary>
    /// Gets the address of the release's files, with a trailing slash.
    /// </summary>
    /// <param name="version">The release.</param>
    /// <returns>The address the platform files and SHA256SUMS sit under.</returns>
    public static Uri BaseUri(string version) => new($"https://github.com/{Repository}/releases/download/wgsidecar-{version}/");

    /// <summary>
    /// Names the platform for an operating system and architecture, using Go's names.
    /// </summary>
    /// <param name="os">The operating system.</param>
    /// <param name="arch">The process architecture.</param>
    /// <returns>The platform, such as linux-amd64, or null when no binary is built for it.</returns>
    public static string? Platform(OSPlatform os, Architecture arch)
    {
        string? osName = os == OSPlatform.Linux ? "linux"
            : os == OSPlatform.OSX ? "darwin"
            : os == OSPlatform.Windows ? "windows"
            : null;
        string? archName = arch switch
        {
            Architecture.X64 => "amd64",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => null
        };

        if (osName is null || archName is null || (archName == "arm" && osName != "linux"))
        {
            return null;
        }

        return $"{osName}-{archName}";
    }

    /// <summary>
    /// Names the release file for a platform.
    /// </summary>
    /// <param name="platform">The platform, such as linux-amd64.</param>
    /// <returns>The file name, such as wgsidecar-linux-amd64, with .exe on Windows.</returns>
    public static string FileName(string platform)
    {
        ArgumentException.ThrowIfNullOrEmpty(platform);
        return "wgsidecar-" + platform + (platform.StartsWith("windows", StringComparison.Ordinal) ? ".exe" : string.Empty);
    }

    /// <summary>
    /// Names this server's platform.
    /// </summary>
    /// <returns>The platform, or null when no binary is built for it.</returns>
    public static string? CurrentPlatform()
    {
        var os = OperatingSystem.IsLinux() ? OSPlatform.Linux
            : OperatingSystem.IsMacOS() ? OSPlatform.OSX
            : OperatingSystem.IsWindows() ? OSPlatform.Windows
            : OSPlatform.FreeBSD;
        return Platform(os, RuntimeInformation.ProcessArchitecture);
    }

    /// <summary>
    /// Finds a file's hash in a SHA256SUMS file.
    /// </summary>
    /// <param name="sums">The SHA256SUMS text, one hash and file name per line.</param>
    /// <param name="fileName">The file.</param>
    /// <returns>The hash in lowercase hex, or null when the file is not listed.</returns>
    public static string? HashOf(string sums, string fileName)
    {
        ArgumentNullException.ThrowIfNull(sums);
        foreach (var raw in sums.Split('\n'))
        {
            var parts = raw.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && string.Equals(parts[1].TrimStart('*').Trim(), fileName, StringComparison.Ordinal) && parts[0].Length == 64)
            {
                return parts[0].ToLowerInvariant();
            }
        }

        return null;
    }
}
