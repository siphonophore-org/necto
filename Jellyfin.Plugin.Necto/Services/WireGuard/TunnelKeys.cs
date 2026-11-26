using System;
using System.Diagnostics;
using System.Security.Cryptography;

namespace Jellyfin.Plugin.Necto.Services.WireGuard;

/// <summary>
/// Makes WireGuard keys. Private keys come from the system's random source; the public key is derived
/// by wgsidecar, since .NET has no X25519.
/// </summary>
public static class TunnelKeys
{
    /// <summary>Makes a new private key, clamped as Curve25519 requires.</summary>
    /// <returns>The key, as base64.</returns>
    public static string NewPrivateKey()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        key[0] &= 248;
        key[31] = (byte)((key[31] & 127) | 64);
        return Convert.ToBase64String(key);
    }

    /// <summary>Makes a new preshared key.</summary>
    /// <returns>The key, as base64.</returns>
    public static string NewPresharedKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>Checks that a key is 32 bytes of base64.</summary>
    /// <param name="key">The key.</param>
    /// <returns>True when the key is well formed.</returns>
    public static bool IsValid(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var buffer = new byte[33];
        return Convert.TryFromBase64String(key.Trim(), buffer, out var written) && written == 32;
    }

    /// <summary>Derives a public key by running <c>wgsidecar pubkey</c>.</summary>
    /// <param name="sidecarPath">The sidecar binary.</param>
    /// <param name="privateKey">The private key, as base64.</param>
    /// <returns>The public key, as base64.</returns>
    /// <exception cref="InvalidOperationException">When the sidecar fails.</exception>
    public static string PublicKey(string sidecarPath, string privateKey)
    {
        var start = new ProcessStartInfo(sidecarPath, "pubkey")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(start) ?? throw new InvalidOperationException("wgsidecar did not start");
        process.StandardInput.WriteLine(privateKey);
        process.StandardInput.Close();

        // The read is bounded too, so a sidecar that hangs is killed rather than holding the save.
        var read = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit(10_000) || !read.Wait(1_000))
        {
            process.Kill(true);
            throw new InvalidOperationException("wgsidecar did not derive the public key in time");
        }

        var output = read.Result.Trim();
        if (process.ExitCode != 0 || !IsValid(output))
        {
            throw new InvalidOperationException("wgsidecar could not derive the public key");
        }

        return output;
    }
}
