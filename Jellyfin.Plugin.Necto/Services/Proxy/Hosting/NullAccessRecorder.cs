using System.Net;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Api.Ddns;

namespace Jellyfin.Plugin.Necto.Services.Proxy.Hosting;

/// <summary>
/// An access recorder that records nothing, for proxies started without one (probes and tests).
/// </summary>
public sealed class NullAccessRecorder : IAccessRecorder
{
    /// <summary>Gets the shared instance.</summary>
    public static IAccessRecorder Instance { get; } = new NullAccessRecorder();

    /// <inheritdoc />
    public void RecordRequest(IPAddress client, string url, string userAgent, bool local)
    {
    }

    /// <inheritdoc />
    public void RecordSignInFailure(IPAddress client, string url, string username = "")
    {
    }
}
