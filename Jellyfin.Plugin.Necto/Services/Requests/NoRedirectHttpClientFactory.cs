using System;
using System.Net;
using System.Net.Http;

namespace Jellyfin.Plugin.Necto.Services.Requests;

/// <summary>
/// Hands out HTTP clients that do not follow redirects, for calls that carry a stored secret, such as Radarr's and
/// Sonarr's API key. A redirect would send the key on to wherever it points, so it comes back as the answer instead.
/// </summary>
public sealed class NoRedirectHttpClientFactory : IHttpClientFactory
{
    // One handler for every client, so connections are pooled and the handler is never disposed with a client.
    private static readonly SocketsHttpHandler Handler = new()
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="NoRedirectHttpClientFactory"/> class.
    /// </summary>
    /// <param name="inner">The server's client factory, kept for the signature the callers use.</param>
    public NoRedirectHttpClientFactory(IHttpClientFactory? inner = null)
    {
        _ = inner;
    }

    /// <inheritdoc />
    public HttpClient CreateClient(string name) => new(Handler, disposeHandler: false);
}
