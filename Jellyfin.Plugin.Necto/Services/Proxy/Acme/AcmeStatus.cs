using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Hosting;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Services.Proxy.Reachability;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
using Jellyfin.Plugin.Necto.Api.Ddns;
using Jellyfin.Plugin.Necto.Api.Proxy;

namespace Jellyfin.Plugin.Necto.Services.Proxy.Acme;

/// <summary>
/// What the dashboard shows about automatic certificates.
/// </summary>
public sealed class AcmeStatus
{
    /// <summary>Gets or sets a value indicating whether Let's Encrypt is being asked right now.</summary>
    public bool Issuing { get; set; }

    /// <summary>Gets or sets why the last attempt failed, empty when it did not.</summary>
    public string LastError { get; set; } = string.Empty;

    /// <summary>Gets or sets when a certificate was last issued (UTC).</summary>
    public DateTime? LastSuccessUtc { get; set; }

    /// <summary>Gets or sets the earliest next attempt after a failure (UTC).</summary>
    public DateTime? NextAttemptUtc { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the last failure was Let's Encrypt's rate limit, which even a
    /// requested certificate waits out until <see cref="NextAttemptUtc"/>.
    /// </summary>
    public bool RateLimited { get; set; }

    /// <summary>Gets or sets the names Let's Encrypt is asked for.</summary>
    public List<string> Names { get; set; } = new();

    /// <summary>Gets or sets how Let's Encrypt checks the names.</summary>
    public AcmeMethod Method { get; set; }

    /// <summary>Gets or sets, for DNS-01, the providers that publish the TXT records.</summary>
    public List<string> DnsProviders { get; set; } = new();
}
