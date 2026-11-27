using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Siphonophore.Stolon;
using MediaBrowser.Model.Activity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
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
using Jellyfin.Plugin.Necto.Services.Notifications;
using Jellyfin.Plugin.Necto.Api.Ddns;
using Jellyfin.Plugin.Necto.Tests.Fakes;

namespace Jellyfin.Plugin.Necto.Tests.Services.Proxy.Reachability;

/// <summary>
/// The checks that the public name points at this server and that its HTTPS port answers from outside the network.
/// No test touches the network.
/// </summary>
public class ReachabilityTests
{
    [Fact]
    public void PublicHostname_IsTheFirstExactPublicName()
    {
        Assert.Equal("media.example.com", ProxyManager.PublicHostname(new ProxySettings { Routes = TestRoutes.Of("*.example.com", "jellyfin.lan", "media.example.com", "b.example.com") }));
        Assert.Equal(string.Empty, ProxyManager.PublicHostname(new ProxySettings { Routes = TestRoutes.Of("*.example.com", "nas.local") }));
    }

    [Fact]
    public void Name_PointingElsewhereIsExplained()
    {
        var status = NameChecker.Compare("media.example.com", new[] { IPAddress.Parse("198.51.100.1") }, "203.0.113.7", string.Empty);
        Assert.False(status.Resolves);
        Assert.Equal("media.example.com points to 198.51.100.1, not to this server at 203.0.113.7. Check its DNS record.", status.Error);
        AssertPlain(status.Error);
    }

    [Fact]
    public void Name_NoAnswerOrUnknownPublicAddressIsExplained()
    {
        var none = NameChecker.Compare("media.example.com", Array.Empty<IPAddress>(), "203.0.113.7", string.Empty);
        Assert.False(none.Resolves);
        Assert.StartsWith("media.example.com does not resolve", none.Error, StringComparison.Ordinal);

        var unknown = NameChecker.Compare("media.example.com", new[] { IPAddress.Parse("198.51.100.1") }, string.Empty, string.Empty);
        Assert.False(unknown.Resolves);
        Assert.Equal(string.Empty, unknown.PublicAddress);
        Assert.StartsWith("This server's public address is not known yet", unknown.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NameChecker_KeepsTheAnswerForAMinute()
    {
        var lookups = 0;
        using var names = new NameChecker(
            (_, _) =>
            {
                lookups++;
                return Task.FromResult(new[] { IPAddress.Parse("203.0.113.7") });
            },
            _ => Task.FromResult(("203.0.113.7", string.Empty)));

        Assert.Equal("media.example.com", names.Cached("media.example.com").Hostname);
        Assert.False(names.Cached("media.example.com").Resolves);
        Assert.True((await names.GetAsync("media.example.com", CancellationToken.None)).Resolves);
        Assert.True((await names.GetAsync("media.example.com", CancellationToken.None)).Resolves);
        Assert.True(names.Cached("media.example.com").Resolves);
        Assert.Equal(1, lookups);

        // Another name is looked up at once.
        await names.GetAsync("other.example.com", CancellationToken.None);
        Assert.Equal(2, lookups);
        Assert.Equal(string.Empty, (await names.GetAsync(string.Empty, CancellationToken.None)).Hostname);
    }

    [Fact]
    public async Task NameChecker_UnknownNameIsAnError()
    {
        using var names = new NameChecker(
            (_, _) => Task.FromException<IPAddress[]>(new SocketException((int)SocketError.HostNotFound)),
            _ => Task.FromResult(("203.0.113.7", string.Empty)));

        var status = await names.GetAsync("media.example.com", CancellationToken.None);

        Assert.False(status.Resolves);
        Assert.StartsWith("media.example.com does not resolve", status.Error, StringComparison.Ordinal);
    }

    // ---- The outside check ----

    private const string StartJson = """
        {"ok":1,"request_id":"1a2b3c4d","permanent_link":"https://check-host.net/check-report/1a2b3c4d",
         "nodes":{"de1.node.check-host.net":["de","Germany","Frankfurt","5.9.1.2","AS24940"],
                  "us2.node.check-host.net":["us","USA","Chicago","5.9.1.3","AS12345"],
                  "jp1.node.check-host.net":["jp","Japan","","5.9.1.4","AS6789"]}}
        """;

    private const string DoneJson = """
        {"de1.node.check-host.net":[{"time":0.031,"address":"203.0.113.7"}],
         "us2.node.check-host.net":[{"error":"Connection timed out"}],
         "jp1.node.check-host.net":[{"error":"Connection refused"}]}
        """;

    [Fact]
    public void Reachability_ReadsTheStart()
    {
        var start = ReachabilityChecker.ParseStart(StartJson);
        Assert.NotNull(start);
        Assert.Equal("1a2b3c4d", start!.Value.RequestId);
        Assert.Equal(
            new[] { ("de1.node.check-host.net", "Frankfurt, Germany"), ("us2.node.check-host.net", "Chicago, USA"), ("jp1.node.check-host.net", "Japan") },
            start.Value.Nodes);
    }

    [Theory]
    [InlineData("{\"error\":\"limit_exceeded\"}")]
    [InlineData("{\"ok\":0,\"request_id\":\"x\"}")]
    [InlineData("{\"ok\":1}")]
    [InlineData("<html>")]
    [InlineData("[]")]
    public void Reachability_StartThatDidNotStartIsNull(string json)
        => Assert.Null(ReachabilityChecker.ParseStart(json));

    [Fact]
    public void Reachability_ReadsPendingSuccessAndFailure()
    {
        var states = ReachabilityChecker.ParseResult("""
            {"a":null,"b":[{"time":0.12,"address":"1.2.3.4"}],"c":[{"error":"Connection timed out"}],"d":[null],"e":[[{"time":0.2}]]}
            """);
        Assert.NotNull(states);
        Assert.Null(states!["a"]);
        Assert.Equal((true, string.Empty), states["b"]);
        Assert.Equal((false, "The connection timed out."), states["c"]);
        Assert.Null(states["d"]);
        Assert.Equal((true, string.Empty), states["e"]);
        Assert.Null(ReachabilityChecker.ParseResult("nope"));
    }

    [Fact]
    public void Reachability_OneLocationConnectingIsEnough()
    {
        var start = ReachabilityChecker.ParseStart(StartJson)!.Value;
        var when = new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
        var result = ReachabilityChecker.Aggregate(start.Nodes, ReachabilityChecker.ParseResult(DoneJson)!, when);

        Assert.True(result.Reachable);
        Assert.Equal(when, result.CheckedUtc);
        Assert.Equal(string.Empty, result.Error);
        Assert.Equal(3, result.Nodes.Count);
        Assert.True(result.Nodes[0].Ok);
        Assert.Equal("Frankfurt, Germany", result.Nodes[0].Location);
        Assert.Equal("The connection timed out.", result.Nodes[1].Error);
        Assert.Equal("The connection was refused.", result.Nodes[2].Error);
    }

    [Fact]
    public void Reachability_NoLocationConnectingIsExplained()
    {
        var start = ReachabilityChecker.ParseStart(StartJson)!.Value;
        var states = ReachabilityChecker.ParseResult("""{"de1.node.check-host.net":[{"error":"Connection timed out"}],"us2.node.check-host.net":null}""")!;
        var result = ReachabilityChecker.Aggregate(start.Nodes, states, DateTime.UtcNow);

        Assert.False(result.Reachable);
        Assert.Equal("No outside location could connect to port 443. Check that the router forwards port 443 to this server.", result.Error);
        Assert.Equal("The check did not finish in time.", result.Nodes[1].Error);
        Assert.Equal("The check did not finish in time.", result.Nodes[2].Error);
        AssertPlain(result.Error);
    }

    [Fact]
    public async Task Reachability_RunsAndKeepsTheResult()
    {
        var asked = new List<Uri>();
        var polls = 0;
        var http = StubHttp.Factory(request =>
        {
            asked.Add(request.RequestUri!);
            Assert.Equal("application/json", request.Headers.Accept.Single().MediaType);
            if (request.RequestUri!.AbsolutePath == "/check-tcp")
            {
                return (HttpStatusCode.OK, StartJson);
            }

            // The first poll is still pending everywhere.
            return (HttpStatusCode.OK, ++polls == 1
                ? """{"de1.node.check-host.net":null,"us2.node.check-host.net":null,"jp1.node.check-host.net":null}"""
                : DoneJson);
        });
        var checker = new ReachabilityChecker(http, (_, _) => Task.CompletedTask);
        Assert.Null(checker.Last.Reachable);
        Assert.Null(checker.Last.CheckedUtc);

        var result = await checker.CheckAsync("media.example.com", CancellationToken.None);

        Assert.True(result.Reachable);
        Assert.Equal(3, result.Nodes.Count);
        Assert.True(checker.Last.Reachable);

        // Only the name and the port leave this server.
        Assert.Equal("https://check-host.net/check-tcp?host=media.example.com:443&max_nodes=3", asked[0].OriginalString);
        Assert.All(asked.Skip(1), u => Assert.Equal("https://check-host.net/check-result/1a2b3c4d", u.OriginalString));
        Assert.Equal(3, asked.Count);
    }

    [Fact]
    public async Task Reachability_ServiceFailuresAreExplained()
    {
        var down = new ReachabilityChecker(StubHttp.Always(HttpStatusCode.BadGateway, "bad gateway"), (_, _) => Task.CompletedTask);
        var result = await down.CheckAsync("media.example.com", CancellationToken.None);
        Assert.Null(result.Reachable);
        Assert.Equal("The outside check could not be reached.", result.Error);
        Assert.NotNull(result.CheckedUtc);

        var busy = new ReachabilityChecker(StubHttp.Always(HttpStatusCode.OK, "{\"error\":\"limit_exceeded\"}"), (_, _) => Task.CompletedTask);
        Assert.Equal("The outside check is busy right now. Try again in a minute.", (await busy.CheckAsync("media.example.com", CancellationToken.None)).Error);

        var throws = new ReachabilityChecker(StubHttp.Factory(_ => throw new HttpRequestException("no route")), (_, _) => Task.CompletedTask);
        Assert.Equal("The outside check could not be reached.", (await throws.CheckAsync("media.example.com", CancellationToken.None)).Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("*.example.com")]
    [InlineData("nas.lan")]
    [InlineData("203.0.113.7")]
    public async Task Reachability_NeedsAPublicName(string hostname)
    {
        var asked = 0;
        var checker = new ReachabilityChecker(StubHttp.Factory(_ => { asked++; return (HttpStatusCode.OK, StartJson); }), (_, _) => Task.CompletedTask);

        var result = await checker.CheckAsync(hostname, CancellationToken.None);

        Assert.Null(result.Reachable);
        Assert.Equal("Add a public address people use from the internet first.", result.Error);
        Assert.Equal(0, asked);
    }

    // ---- On-screen text ----

    // No semicolons or parentheses, and at most two sentences.
    private static void AssertPlain(string text)
    {
        Assert.DoesNotContain(";", text, StringComparison.Ordinal);
        Assert.DoesNotContain("(", text, StringComparison.Ordinal);
        Assert.DoesNotContain(")", text, StringComparison.Ordinal);
        var sentences = text.Split(". ", StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.True(sentences <= 2, text);
    }
}
