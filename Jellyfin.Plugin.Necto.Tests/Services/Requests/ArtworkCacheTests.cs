using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using MediaBrowser.Controller;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Requests;

/// <summary>
/// The artwork cache reads TMDb's images a few at a time, keeps the reads going when an asker gives up, and frees
/// their places once they finish.
/// </summary>
public class ArtworkCacheTests
{
    /// <summary>
    /// Giving up on images never starts more than eight reads, and a read under way is joined rather than started again.
    /// True: a page scrolled past quickly, or anyone hammering the address, holds at most eight reads of TMDb.
    /// False: abandoned reads would pile up without limit.
    /// </summary>
    [Fact]
    public async Task GetAsync_GivenUpOn_NeverStartsMoreThanEightReads()
    {
        var folder = Path.Join(Path.GetTempPath(), "necto-artwork-" + Guid.NewGuid().ToString("N"));
        var http = new HeldImages();
        using var cache = new ArtworkCache(http, InterfaceStub.Of<IServerApplicationPaths>(("get_CachePath", _ => folder)), NullLogger<ArtworkCache>.Instance);
        try
        {
            for (var i = 0; i < 8; i++)
            {
                using var gaveUp = new CancellationTokenSource();
                var asked = cache.GetAsync("w342", "gone" + i + ".jpg", gaveUp.Token);
                await gaveUp.CancelAsync();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => asked);
            }

            // Their reads still run, so another image is sent on to TMDb rather than read as well.
            Assert.Null(await cache.GetAsync("w342", "another.jpg", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(8, Volatile.Read(ref http.Started));

            var joined = cache.GetAsync("w342", "gone3.jpg", CancellationToken.None);
            http.Answer.SetResult();
            Assert.Equal(Path.Combine(cache.Folder, "w342", "gone3.jpg"), await joined);
            Assert.Equal(8, Volatile.Read(ref http.Started));

            string? another = null;
            for (var wait = 0; wait < 200 && another is null; wait++)
            {
                another = await cache.GetAsync("w342", "another.jpg", CancellationToken.None);
                if (another is null)
                {
                    await Task.Delay(25);
                }
            }

            Assert.NotNull(another);
        }
        finally
        {
            http.Answer.TrySetResult();
            await DeleteWhenSettledAsync(folder);
        }
    }

    // The reads outlive the askers who gave up, so one may still be writing its file when the test ends.
    private static async Task DeleteWhenSettledAsync(string folder)
    {
        for (var attempt = 0; Directory.Exists(folder); attempt++)
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException) when (attempt < 40)
            {
                await Task.Delay(50);
            }
        }
    }

    // Holds every image read open until told to answer, counting how many were started.
    private sealed class HeldImages : HttpMessageHandler, IHttpClientFactory
    {
        public readonly TaskCompletionSource Answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Started;

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Started);
            await Answer.Task.ConfigureAwait(false);
            var content = new ByteArrayContent(new byte[] { 1, 2, 3 });
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }
    }
}
