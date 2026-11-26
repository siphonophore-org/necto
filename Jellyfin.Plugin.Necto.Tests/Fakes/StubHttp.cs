using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Necto.Tests.Fakes;

/// <summary>
/// Builds an <see cref="IHttpClientFactory"/> whose clients answer with canned responses, so a provider's request
/// shaping and response parsing can be exercised without the network.
/// </summary>
public static class StubHttp
{
    /// <summary>Builds a factory whose every client replies through <paramref name="responder"/>.</summary>
    /// <param name="responder">Maps a request to a status code and body.</param>
    /// <returns>A stub HTTP client factory.</returns>
    public static StubHttpFactory Factory(Func<HttpRequestMessage, (HttpStatusCode Code, string Body)> responder)
        => new(() => new CannedHandler(responder));

    /// <summary>Builds a factory that returns the same status and body for every request.</summary>
    /// <param name="code">The status code to return.</param>
    /// <param name="body">The response body to return.</param>
    /// <returns>A stub HTTP client factory.</returns>
    public static StubHttpFactory Always(HttpStatusCode code, string body)
        => Factory(_ => (code, body));

    /// <summary>Builds a factory whose clients send through a handler the test gives.</summary>
    /// <param name="handler">Makes the handler for each client.</param>
    /// <returns>A stub HTTP client factory.</returns>
    public static StubHttpFactory WithHandler(Func<HttpMessageHandler> handler) => new(handler);

    private sealed class CannedHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, (HttpStatusCode Code, string Body)> _responder;

        public CannedHandler(Func<HttpRequestMessage, (HttpStatusCode Code, string Body)> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var (code, body) = _responder(request);
            return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body), RequestMessage = request });
        }
    }
}

/// <summary>
/// An <see cref="IHttpClientFactory"/> that records the client names asked for and hands out clients over a test
/// handler.
/// </summary>
public sealed class StubHttpFactory : IHttpClientFactory
{
    private readonly Func<HttpMessageHandler> _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="StubHttpFactory"/> class.
    /// </summary>
    /// <param name="handler">Makes the handler for each client.</param>
    public StubHttpFactory(Func<HttpMessageHandler> handler) => _handler = handler;

    /// <summary>Gets the names each client was asked for under, in order.</summary>
    public List<string> Created { get; } = new();

    /// <inheritdoc />
    public HttpClient CreateClient(string name)
    {
        lock (Created)
        {
            Created.Add(name);
        }

        return new HttpClient(_handler());
    }
}
