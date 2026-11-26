using System;
using System.Collections.Generic;
using System.IO;
using Siphonophore.Stolon;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Hosting;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Services.Proxy.Reachability;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
using Jellyfin.Plugin.Necto.Services.Notifications;
using Jellyfin.Plugin.Necto.Api.Ddns;
using Jellyfin.Plugin.Necto.Api.Proxy;
using Jellyfin.Plugin.Necto.Tests.Fakes;

namespace Jellyfin.Plugin.Necto.Tests.Services.Ddns;

/// <summary>
/// The order the takeover writes in, so a crash at any point still leaves Undo what it needs.
/// </summary>
public sealed class OrchestratorTests : IDisposable
{
    private readonly string _dir = Path.Join(Path.GetTempPath(), "orchestrator-" + Guid.NewGuid().ToString("N"));
    private readonly FakeJellyfinHost _host;

    public OrchestratorTests()
    {
        Directory.CreateDirectory(_dir);
        _host = new FakeJellyfinHost(_dir, started: false);
    }

    public void Dispose() => Directory.Delete(_dir, true);

    private JellyfinNetworkOrchestrator Orchestrator() => new(
        _host.Configuration,
        _host.System,
        _host.Lifetime,
        new ProxyPaths(_dir),
        null,
        NullLogger<JellyfinNetworkOrchestrator>.Instance);

    private static ProxySettings Settings(bool loopbackOnly) => new()
    {
        Enabled = true,
        Routes = TestRoutes.Of("media.example.com"),
        Advanced = new ProxyAdvancedSettings { JellyfinLoopbackOnly = loopbackOnly }
    };

    private static NetworkConfiguration Network() => new() { EnableIPv4 = true, LocalNetworkAddresses = new[] { "192.168.1.10" } };

    [Fact]
    public void Commit_SavesACopyOfJellyfinsNetworkFileOnce()
    {
        File.WriteAllText(Path.Join(_dir, "network.xml"), "original");
        var orchestrator = Orchestrator();
        orchestrator.Commit(Settings(false), Network());
        File.WriteAllText(Path.Join(_dir, "network.xml"), "changed");
        orchestrator.Commit(Settings(true), Network());

        Assert.Equal("original", File.ReadAllText(Path.Join(_dir, JellyfinNetworkOrchestrator.BackupFileName)));
    }

    [Fact]
    public void Commit_TurningLoopbackOff_KeepsTheRecordUntilJellyfinsSettingsAreSaved()
    {
        var orchestrator = Orchestrator();
        var network = Network();
        orchestrator.Commit(Settings(true), network);
        Assert.True(orchestrator.State.Snapshot!.BoundToLoopback);

        // Jellyfin's settings fail to save, as a crash at that moment would leave them.
        var fail = true;
        _host.OnSave = (key, _) =>
        {
            if (fail && key == "network")
            {
                throw new IOException("disk full");
            }
        };
        Assert.Throws<IOException>(() => orchestrator.Commit(Settings(false), network));
        Assert.True(orchestrator.State.Snapshot!.BoundToLoopback);
        Assert.NotEmpty(orchestrator.State.Snapshot!.WrittenLocal);

        fail = false;
        orchestrator.Commit(Settings(false), network);
        Assert.False(orchestrator.State.Snapshot!.BoundToLoopback);
        Assert.Equal(new[] { "192.168.1.10", "127.0.0.1" }, network.LocalNetworkAddresses);
    }
}
