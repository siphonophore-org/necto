using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Siphonophore.Stolon;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Hosting;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Services.Proxy.Reachability;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
using Jellyfin.Plugin.Necto.Api.Ddns;

namespace Jellyfin.Plugin.Necto.Services.Proxy;

/// <summary>
/// Normalizes proxy settings and reports what is wrong with them. Normalizing first means a save never
/// stores half-valid values: hostnames are lower-cased, numbers are clamped and lists are cleaned.
/// </summary>
public static partial class ProxySettingsValidator
{
    /// <summary>
    /// Cleans the settings in place: normalizes hostnames and networks and clamps numbers to sane ranges.
    /// Empty advanced values stay empty, so they keep following the defaults.
    /// </summary>
    /// <param name="settings">The settings to clean.</param>
    public static void Normalize(ProxySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Routes = (settings.Routes ?? new List<ProxyRoute>()).Where(r => r is not null).ToList();
        foreach (var route in settings.Routes)
        {
            NormalizeRoute(route);
        }

        settings.Visitors ??= new VisitorSettings();
        settings.Visitors.HistoryDays = Math.Clamp(settings.Visitors.HistoryDays, 1, 3650);
        var a = settings.Advanced ??= new ProxyAdvancedSettings();
        a.LanAddress = (a.LanAddress ?? string.Empty).Trim();
        a.TrustedNetworks = CleanEntries(a.TrustedNetworks);
        a.BlockedAddresses = CleanEntries(a.BlockedAddresses);
        a.Countries = (a.Countries ?? new List<string>())
            .Select(c => (c ?? string.Empty).Trim().ToUpperInvariant())
            .Where(c => c.Length == 2 && c.All(char.IsAsciiLetterUpper))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        a.HstsMaxAgeDays = ClampOptional(a.HstsMaxAgeDays, 1, 730);
        a.KeepAliveSeconds = ClampOptional(a.KeepAliveSeconds, 5, 3600);
        a.RequestHeadersTimeoutSeconds = ClampOptional(a.RequestHeadersTimeoutSeconds, 1, 300);
        a.MaxRequestHeadersKb = ClampOptional(a.MaxRequestHeadersKb, 8, 1024);
        a.MaxConnections = ClampOptional(a.MaxConnections, 0, int.MaxValue);
        a.MaxConnectionsPerIp = ClampOptional(a.MaxConnectionsPerIp, 0, int.MaxValue);
        a.BanFailures = ClampOptional(a.BanFailures, 0, 1000);
        a.BanWindowMinutes = ClampOptional(a.BanWindowMinutes, 1, 1440);
        a.BanMinutes = ClampOptional(a.BanMinutes, 0, 525600);
        a.MaxRequestBodyMbWan = ClampOptional(a.MaxRequestBodyMbWan, 0, int.MaxValue);
        a.MaxRequestBodyMbLan = ClampOptional(a.MaxRequestBodyMbLan, 0, int.MaxValue);
        a.MinResponseBytesPerSecond = ClampOptional(a.MinResponseBytesPerSecond, 0, int.MaxValue);
        a.BackendActivityTimeoutSeconds = ClampOptional(a.BackendActivityTimeoutSeconds, 61, 86400);
    }

    /// <summary>
    /// Cleans one route: the host lower-cased, the upstream and file paths trimmed, and a missing id or
    /// certificate filled in.
    /// </summary>
    /// <param name="route">The route, changed in place.</param>
    public static void NormalizeRoute(ProxyRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (string.IsNullOrEmpty(route.Id) || route.Id.Length > 64 || !route.Id.All(char.IsAsciiLetterOrDigit))
        {
            route.Id = Guid.NewGuid().ToString("N");
        }

        route.Id = route.Id.ToLowerInvariant();
        var normalized = HostnameMatcher.Normalize(route.Host);
        route.Host = normalized.Length > 0 ? normalized : (route.Host ?? string.Empty).Trim();
        route.Upstream = (route.Upstream ?? string.Empty).Trim().TrimEnd('/');
        route.DdnsHostname = HostnameMatcher.Normalize(route.DdnsHostname);
        var c = route.Certificate ??= new ProxyCertificateSettings();
        c.Path = (c.Path ?? string.Empty).Trim();
        c.KeyPath = (c.KeyPath ?? string.Empty).Trim();
        c.Password ??= string.Empty;
        c.DnsRecordId = (c.DnsRecordId ?? string.Empty).Trim();
    }

    /// <summary>
    /// Reads a route's upstream address. Only http and https addresses with a host are taken, and nothing after
    /// the port, since the proxy forwards each path as it came.
    /// </summary>
    /// <param name="upstream">The address, such as <c>http://192.168.1.20:8096</c>.</param>
    /// <returns>The address, or <c>null</c> when it is not one.</returns>
    public static Uri? ParseUpstream(string? upstream)
    {
        if (string.IsNullOrWhiteSpace(upstream)
            || !Uri.TryCreate(upstream.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.AbsolutePath.Length > 1
            || uri.Query.Length > 0)
        {
            return null;
        }

        return new UriBuilder(uri.Scheme, uri.Host, uri.Port).Uri;
    }

    /// <summary>
    /// Reports what is wrong with one route, among the others.
    /// </summary>
    /// <param name="route">The normalized route.</param>
    /// <param name="others">The other routes, which may not share its host.</param>
    /// <param name="language">The page's language, or <c>null</c> for the server's.</param>
    /// <returns>Each problem as a sentence, empty when the route can be saved.</returns>
    public static List<string> ValidateRoute(ProxyRoute route, IEnumerable<ProxyRoute> others, string? language = null)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(others);
        var errors = new List<string>();
        if (route.Host.Length == 0)
        {
            errors.Add(Text(language, TranslationKeys.ErrorEnterHostName));
        }
        else if (HostnameMatcher.Normalize(route.Host).Length == 0)
        {
            errors.Add(Text(language, TranslationKeys.ProxyErrorHostname, route.Host));
        }
        else if (others.Any(o => !string.Equals(o.Id, route.Id, StringComparison.Ordinal) && string.Equals(HostnameMatcher.Normalize(o.Host), route.Host, StringComparison.Ordinal)))
        {
            errors.Add(Text(language, TranslationKeys.ProxyErrorRouteHostTaken, route.Host));
        }

        if (route.Host.Length > 0 && !FitsDdnsName(route.Host, route.DdnsHostname))
        {
            errors.Add(route.DdnsHostname.StartsWith("*.", StringComparison.Ordinal)
                ? Text(language, TranslationKeys.ProxyErrorDdnsSubdomain, route.DdnsHostname[2..])
                : Text(language, TranslationKeys.ProxyErrorDdnsHost, route.DdnsHostname));
        }

        if (!route.IsJellyfin && ParseUpstream(route.Upstream) is null)
        {
            errors.Add(Text(language, TranslationKeys.ProxyErrorUpstream, route.Upstream));
        }

        CheckEnum(errors, language, Text(language, TranslationKeys.ProxyErrorFieldCertificateSource), route.Certificate.Mode);
        CheckEnum(errors, language, Text(language, TranslationKeys.ProxyLabelChallenge), route.Certificate.Challenge);
        CheckCertificate(errors, language, route.Certificate);

        // A wildcard can only be proved over DNS, and so can a name the internet cannot reach.
        if (route.Certificate.Mode == CertificateMode.Automatic && route.Certificate.Challenge == CertificateChallenge.Http
            && (route.Host.StartsWith('*') || route.LocalOnly))
        {
            errors.Add(Text(language, TranslationKeys.ProxyErrorHttpChallengeNeedsInternet));
        }

        return errors;
    }

    /// <summary>
    /// Tells whether a host fits the DDNS name a route is locked to: the name itself, or for a wildcard such as
    /// <c>*.example.com</c>, the wildcard or one name under it, such as <c>media.example.com</c>.
    /// </summary>
    /// <param name="host">The normalized host.</param>
    /// <param name="ddnsHostname">The normalized DDNS name, or empty for none.</param>
    /// <returns><c>true</c> when the host fits, always with no DDNS name.</returns>
    public static bool FitsDdnsName(string host, string? ddnsHostname)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (string.IsNullOrEmpty(ddnsHostname))
        {
            return true;
        }

        if (!ddnsHostname.StartsWith("*.", StringComparison.Ordinal))
        {
            return string.Equals(host, ddnsHostname, StringComparison.Ordinal);
        }

        var suffix = ddnsHostname[1..];
        if (!host.EndsWith(suffix, StringComparison.Ordinal))
        {
            return false;
        }

        var label = host[..^suffix.Length];
        return label == "*" || DnsLabel().IsMatch(label);
    }

    [System.Text.RegularExpressions.GeneratedRegex("^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$")]
    private static partial System.Text.RegularExpressions.Regex DnsLabel();

    /// <summary>
    /// Lists every problem with already-normalized settings. Empty means valid.
    /// </summary>
    /// <param name="settings">The normalized settings.</param>
    /// <param name="language">The language of the problems, or <c>null</c> for the server's.</param>
    /// <returns>Human-readable problems.</returns>
    public static List<string> Validate(ProxySettings settings, string? language = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var errors = new List<string>();

        // Enum values can arrive as any number. One that names nothing would fail to save as XML, which would
        // leave Jellyfin unable to store the plugin's settings at all, so it is refused here.
        CheckEnum(errors, language, Text(language, TranslationKeys.ProxyLabelCountryRule), settings.Advanced.CountryMode ?? CountryRuleMode.Off);

        var o = ProxyOptions.From(settings.Advanced);
        CheckPort(errors, language, Text(language, TranslationKeys.ProxyLabelHttpPort), o.HttpPort);
        CheckPort(errors, language, Text(language, TranslationKeys.ProxyLabelHttpsPort), o.HttpsPort);
        if (o.HttpPort == o.HttpsPort)
        {
            errors.Add(Text(language, TranslationKeys.ProxyErrorSamePorts));
        }

        // Local clients are then sent to the public address, so there has to be one that is not a wildcard.
        if (o.JellyfinLoopbackOnly && !ProxyManager.JellyfinHostnames(settings).Any(h => !h.StartsWith('*')))
        {
            errors.Add(Text(language, TranslationKeys.ProxyErrorLoopbackNeedsAddress));
        }

        if (o.LanAddress.Length > 0 && !IPAddress.TryParse(o.LanAddress, out _))
        {
            errors.Add(Text(language, TranslationKeys.ProxyErrorLanAddress, o.LanAddress));
        }

        if (o.CountryMode == CountryRuleMode.AllowOnly && o.Countries.Count == 0)
        {
            errors.Add(Text(language, TranslationKeys.ProxyErrorAllowOnlyNeedsCountry));
        }

        foreach (var raw in o.TrustedNetworks.Where(n => !NetworkClassifier.TryParseNetwork(n, out _)))
        {
            errors.Add(Text(language, TranslationKeys.ProxyErrorTrustedNetwork, raw));
        }

        foreach (var raw in o.BlockedAddresses.Where(n => !NetworkClassifier.TryParseNetwork(n, out _)))
        {
            errors.Add(Text(language, TranslationKeys.ProxyErrorBlockedAddress, raw));
        }

        CheckEnum(errors, language, Text(language, TranslationKeys.HeaderLocations), settings.Visitors.Location);
        return errors;
    }

    private static List<string> CleanEntries(List<string>? values)
        => (values ?? new List<string>())
            .Select(n => (n ?? string.Empty).Trim())
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static int? ClampOptional(int? value, int min, int max)
        => value is { } v ? Math.Clamp(v, min, max) : null;

    private static void CheckEnum<T>(List<string> errors, string? language, string label, T value)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            errors.Add(Text(language, TranslationKeys.ProxyErrorUnknownValue, label));
        }
    }

    private static void CheckPort(List<string> errors, string? language, string label, int port)
    {
        if (port is < 1 or > 65535)
        {
            errors.Add(Text(language, TranslationKeys.ProxyErrorPortRange, label));
        }
    }

    private static void CheckCertificate(List<string> errors, string? language, ProxyCertificateSettings certificate)
    {
        switch (certificate.Mode)
        {
            case CertificateMode.Pkcs12File when certificate.Path.Length == 0:
                errors.Add(Text(language, TranslationKeys.ProxyErrorChoosePkcs12));
                break;
            case CertificateMode.PemFiles when certificate.Path.Length == 0 || certificate.KeyPath.Length == 0:
                errors.Add(Text(language, TranslationKeys.ProxyErrorChoosePemFiles));
                break;
        }
    }

    // One string in the language asked for, else the server's.
    private static string Text(string? language, string key, params object?[] args)
        => StolonTranslations.Format(key, language ?? ServerText.Language, args);
}
