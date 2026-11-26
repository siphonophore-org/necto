using System.Net;
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
/// Where the proxy reports which address each client used, for the history. Kept to plain values so the
/// proxy host never touches Jellyfin's services.
/// </summary>
public interface IAccessRecorder
{
    /// <summary>
    /// Notes that a client reached Jellyfin through an address.
    /// </summary>
    /// <param name="client">The client's IP address.</param>
    /// <param name="url">The address it used, like <c>https://media.example.com:8920</c>.</param>
    /// <param name="userAgent">The client's User-Agent header.</param>
    /// <param name="local">Whether the client is on the local or a trusted network.</param>
    void RecordRequest(IPAddress client, string url, string userAgent, bool local);

    /// <summary>
    /// Notes a refused sign-in, which Jellyfin itself does not report when the username does not exist.
    /// </summary>
    /// <param name="client">The client's IP address.</param>
    /// <param name="url">The address it used.</param>
    /// <param name="username">The username it tried, or empty when the request named none.</param>
    void RecordSignInFailure(IPAddress client, string url, string username = "");
}
