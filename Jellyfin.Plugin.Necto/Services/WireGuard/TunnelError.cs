using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Necto.Services.WireGuard;

/// <summary>
/// Turns the errors the sidecar reports into short sentences people can act on.
/// </summary>
public static partial class TunnelError
{
    /// <summary>
    /// Rewords a known sidecar error, and returns any other one as it is.
    /// </summary>
    /// <param name="error">The error the sidecar reported, or null.</param>
    /// <returns>The error to show, or null.</returns>
    public static string? Plain(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return error;
        }

        var inUse = PortInUse().Match(error);
        if (inUse.Success)
        {
            return $"Port {inUse.Groups[1].Value} is already in use by another program.";
        }

        return error;
    }

    [GeneratedRegex(@"listen udp\d? [^\s]*:(\d+): bind: address already in use")]
    private static partial Regex PortInUse();
}
