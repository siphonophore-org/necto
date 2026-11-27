using System;
using System.Net.Http;

namespace Jellyfin.Plugin.Necto.Tests.Fakes.CustomPages;

/// <summary>
/// Hands out clients a test builds, such as one over a stub handler, so a test never reaches the network.
/// </summary>
public sealed class StubHttpClientFactory : IHttpClientFactory
{
    private readonly Func<HttpClient> _create;

    public StubHttpClientFactory(Func<HttpClient> create)
    {
        _create = create;
    }

    public StubHttpClientFactory()
        : this(() => throw new InvalidOperationException("No client was expected."))
    {
    }

    public int Created { get; private set; }

    public HttpClient CreateClient(string name)
    {
        Created++;
        return _create();
    }
}
