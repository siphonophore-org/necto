using System;
using System.Collections.Generic;
using System.Net.Security;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Services.Proxy.Reachability;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
using Jellyfin.Plugin.Necto.Api.Ddns;
using Jellyfin.Plugin.Necto.Api.Proxy;

namespace Jellyfin.Plugin.Necto.Services.Proxy.Hosting;

/// <summary>
/// The cipher suites the proxy offers: forward-secret key exchange with AES-GCM or ChaCha20-Poly1305, and nothing
/// else. The system's own list also has the older CBC suites, which every client that can speak TLS 1.2 with
/// these has long since stopped needing.
/// </summary>
public static class TlsCiphers
{
    /// <summary>
    /// Gets the suites offered, strongest first. RSA key exchange is listed for an uploaded RSA certificate;
    /// the ones the plugin requests are ECDSA.
    /// </summary>
    public static IReadOnlyList<TlsCipherSuite> Suites { get; } = new[]
    {
        TlsCipherSuite.TLS_AES_256_GCM_SHA384,
        TlsCipherSuite.TLS_AES_128_GCM_SHA256,
        TlsCipherSuite.TLS_CHACHA20_POLY1305_SHA256,
        TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384,
        TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256,
        TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_CHACHA20_POLY1305_SHA256,
        TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384,
        TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256,
        TlsCipherSuite.TLS_ECDHE_RSA_WITH_CHACHA20_POLY1305_SHA256
    };

    /// <summary>
    /// Gets the policy to hand to TLS, or null to leave the system's list in place. Windows picks suites
    /// system-wide and has no per-connection policy, and a platform that refuses this one keeps its own list
    /// rather than losing HTTPS.
    /// </summary>
    public static CipherSuitesPolicy? Policy { get; } = Create();

    private static CipherSuitesPolicy? Create()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS() && !OperatingSystem.IsFreeBSD())
        {
            return null;
        }

        try
        {
#pragma warning disable CA1416 // Guarded by the platform test above.
            return new CipherSuitesPolicy(Suites);
#pragma warning restore CA1416
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or NotSupportedException)
        {
            return null;
        }
    }
}
