using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Hosting;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
namespace Jellyfin.Plugin.Necto.Services.Proxy;

/// <summary>
/// The outcome of <see cref="RequestPolicy.Decide"/>.
/// </summary>
/// <param name="Action">What to do.</param>
/// <param name="Value">The redirect URL or challenge answer.</param>
/// <param name="Route">The route a forwarded request goes to, or <c>null</c>.</param>
public readonly record struct RequestDecision(RequestAction Action, string Value = "", RuntimeRoute? Route = null);
