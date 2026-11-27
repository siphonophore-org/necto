using System;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Api.Ddns;

namespace Jellyfin.Plugin.Necto.Services.Proxy.Visitors;

/// <summary>
/// A refused sign-in the proxy saw, waiting for Jellyfin's own failure event.
/// </summary>
/// <param name="Ip">The client IP.</param>
/// <param name="Url">The address used.</param>
/// <param name="At">When (UTC).</param>
/// <param name="Username">The username tried, or empty when unknown.</param>
public sealed record PendingFailure(string Ip, string Url, DateTime At, string Username = "");
