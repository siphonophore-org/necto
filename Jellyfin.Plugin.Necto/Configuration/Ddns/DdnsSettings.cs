using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace Jellyfin.Plugin.Necto.Configuration.Ddns;

/// <summary>
/// The DDNS tool's records and its settings. The public IP lookup is shared by every record.
/// </summary>
public class DdnsSettings
{
    /// <summary>Gets or sets a value indicating whether DDNS is on. While it is off, no record is updated.</summary>
    public bool Enabled { get; set; }

    /// <summary>The default IPv4 detection endpoints, seeded into the settings so each is visible and editable.</summary>
    public static readonly string[] DefaultIPv4Endpoints =
    {
        "https://api.ipify.org",
        "https://ipv4.icanhazip.com",
        "https://checkip.amazonaws.com",
        "https://v4.ident.me"
    };

    /// <summary>The default IPv6 detection endpoints, seeded into the settings so each is visible and editable.</summary>
    public static readonly string[] DefaultIPv6Endpoints =
    {
        "https://api6.ipify.org",
        "https://ipv6.icanhazip.com",
        "https://v6.ident.me"
    };

    /// <summary>Gets or sets the DNS records kept pointed at the server's public IP. Only the DDNS routes change them.</summary>
    public List<DNSRecord> Records { get; set; } = new();

    /// <summary>Gets or sets the endpoints asked for the public IPv4 address, one per line.</summary>
    public string IPv4DetectionUrl { get; set; } = string.Join("\n", DefaultIPv4Endpoints);

    /// <summary>Gets or sets the endpoints asked for the public IPv6 address, one per line.</summary>
    public string IPv6DetectionUrl { get; set; } = string.Join("\n", DefaultIPv6Endpoints);

    /// <summary>Gets or sets how many hours may pass before a record is pushed again with an unchanged address. 0 never pushes an unchanged one.</summary>
    public int ForceUpdateHours { get; set; }

    /// <summary>Gets or sets a value indicating whether a detected private, CGNAT or reserved address is refused rather than published.</summary>
    public bool SkipInternalAddresses { get; set; } = true;

    /// <summary>Gets or sets how many failures in a row pause a record. 0 never pauses.</summary>
    public int BackoffAfterFailures { get; set; } = 3;

    /// <summary>Gets or sets how many hours a paused record waits before one more try.</summary>
    public int BackoffHours { get; set; } = 24;

    /// <summary>Gets or sets how many seconds one detection or provider request may take.</summary>
    public int RequestTimeoutSeconds { get; set; } = 15;

    /// <summary>Gets or sets the public IPv4 address the last run saw. Kept in the status store, never in the settings file.</summary>
    [XmlIgnore]
    public string LastDetectedIPv4 { get; set; } = string.Empty;

    /// <summary>Gets or sets the public IPv6 address the last run saw. Kept in the status store, never in the settings file.</summary>
    [XmlIgnore]
    public string LastDetectedIPv6 { get; set; } = string.Empty;

    /// <summary>Gets or sets the warning from the last run when a wanted family had no public address, or empty.</summary>
    [XmlIgnore]
    public string LastDetectionMessage { get; set; } = string.Empty;

    /// <summary>
    /// Gives the timeout for one detection or provider request, clamped so a huge setting cannot overflow the timer.
    /// </summary>
    /// <returns>The timeout.</returns>
    public TimeSpan RequestTimeout()
        => TimeSpan.FromSeconds(RequestTimeoutSeconds > 0 ? Math.Min(RequestTimeoutSeconds, 300) : 15);
}
