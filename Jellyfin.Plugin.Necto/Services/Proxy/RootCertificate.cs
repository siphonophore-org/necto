using System;
using Jellyfin.Plugin.Necto.Configuration;
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
using Jellyfin.Plugin.Necto.Api.Proxy;

namespace Jellyfin.Plugin.Necto.Services.Proxy;

/// <summary>
/// The public part of the self-signed option's root, which devices install to trust the proxy's certificate.
/// </summary>
/// <param name="Pem">The certificate, PEM encoded; no private key.</param>
/// <param name="Fingerprint">Its SHA-256 fingerprint, as devices show it.</param>
/// <param name="NotAfterUtc">When it ends.</param>
public sealed record RootCertificate(string Pem, string Fingerprint, DateTime NotAfterUtc);
