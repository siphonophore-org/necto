using System.Collections.Generic;

namespace Jellyfin.Plugin.Necto.Services.Notifications;

/// <summary>
/// Every event the Proxy tool sends to Notifications, so Notifications can list them.
/// </summary>
public static class ProxyEvents
{
    /// <summary>An internet address was banned for failed sign-ins.</summary>
    public const string Banned = "proxy.banned";

    /// <summary>A route's certificate expires within two weeks and is not being renewed.</summary>
    public const string CertificateExpiring = "proxy.certificate_expiring";

    /// <summary>A route's certificate could not be issued, renewed, or loaded.</summary>
    public const string CertificateFailed = "proxy.certificate_failed";

    /// <summary>Let's Encrypt issued or renewed a route's certificate.</summary>
    public const string CertificateIssued = "proxy.certificate_issued";

    /// <summary>The proxy could not start, such as when another program holds its ports.</summary>
    public const string StartFailed = "proxy.start_failed";

    /// <summary>The scheduled check from the internet could not reach the HTTPS port.</summary>
    public const string Unreachable = "proxy.unreachable";

    /// <summary>Most visitors look local, so something in front of the proxy hides their addresses.</summary>
    public const string VisitorsHidden = "proxy.visitors_hidden";

    /// <summary>
    /// Gets every event key with the key of its name, in the order Notifications lists them.
    /// </summary>
    public static IReadOnlyDictionary<string, string> All { get; } = new Dictionary<string, string>
    {
        [Banned] = "ProxyEventBanned",
        [CertificateExpiring] = "ProxyEventCertificateExpiring",
        [CertificateFailed] = "ProxyTagCertificateFailed",
        [CertificateIssued] = "ProxyEventCertificateIssued",
        [StartFailed] = "ProxyEventStartFailed",
        [Unreachable] = "ProxyEventUnreachable",
        [VisitorsHidden] = "ProxyEventVisitorsHidden"
    };
}
