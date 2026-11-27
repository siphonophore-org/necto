using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Necto.Tests.Fakes;

/// <summary>
/// Answers Radarr at radarr.test, TMDb, and TMDb's images, noting every question asked, so a test sees what the
/// Requests services asked of the outside world.
/// </summary>
public sealed class OutsideServers : HttpMessageHandler, IHttpClientFactory
{
    /// <summary>Gets every question asked, as its host and path, with the query for Radarr.</summary>
    public ConcurrentQueue<string> Asked { get; } = new();

    public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        var path = uri.AbsolutePath;
        Asked.Enqueue(uri.Host + path + (uri.Host == "radarr.test" ? uri.Query : string.Empty));
        var body = uri.Host == "radarr.test"
            ? uri.Query.Length == 0
                ? """[{"tmdbId":603,"hasFile":true},{"tmdbId":604,"hasFile":false}]"""
                : "[]"
            : uri.Host == "image.tmdb.org" ? null
            : path.Contains("/genre/", StringComparison.Ordinal) ? """{"genres":[{"id":18,"name":"Drama"}]}"""
            : path.StartsWith("/3/search/", StringComparison.Ordinal) ? """{"results":[{"id":9,"name":"Anime"}]}"""
            : path.StartsWith("/3/tv/", StringComparison.Ordinal) ? """{"id":7,"name":"Show","number_of_seasons":3,"external_ids":{"tvdb_id":500}}"""
            : """{"results":[{"id":7,"name":"Show","title":"Movie","genre_ids":[18],"poster_path":"/p7.jpg"}],"total_pages":1,"total_results":1}""";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = body is null
                ? new ByteArrayContent(new byte[] { 1, 2, 3 }) { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg") } }
                : new StringContent(body, Encoding.UTF8, "application/json")
        });
    }
}
