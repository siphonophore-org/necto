using Microsoft.AspNetCore.Http;
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
/// The parts of a request the policy looks at.
/// </summary>
/// <param name="Local">Whether the client is on the LAN or a trusted network.</param>
/// <param name="IsHttps">Whether the request arrived on the HTTPS listener.</param>
/// <param name="Host">The Host header, without port.</param>
/// <param name="Method">The HTTP method.</param>
/// <param name="Path">The path, including Jellyfin's base URL.</param>
/// <param name="Query">The raw query string.</param>
public readonly record struct RequestFacts(bool Local, bool IsHttps, string Host, string Method, PathString Path, QueryString Query);
