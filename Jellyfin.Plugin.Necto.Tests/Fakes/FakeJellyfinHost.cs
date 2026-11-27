using System;
using System.Linq;
using System.Threading;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.Necto.Tests.Fakes;

/// <summary>
/// Stand-ins for the parts of Jellyfin trusting the proxy writes and restarts: its configuration manager, its system
/// manager, and its host lifetime. Every save and restart is recorded, and a test may hook either.
/// </summary>
public sealed class FakeJellyfinHost
{
    private readonly InterfaceStub _configurationStub;
    private readonly InterfaceStub _systemStub;
    private object? _network;

    /// <summary>
    /// Initializes a new instance of the <see cref="FakeJellyfinHost"/> class.
    /// </summary>
    /// <param name="dir">The folder Jellyfin's configuration files live in.</param>
    /// <param name="started">Whether Jellyfin has finished starting.</param>
    public FakeJellyfinHost(string dir, bool started = true)
    {
        (Configuration, _configurationStub) = InterfaceStub.Create<IConfigurationManager>();
        var paths = Stubs.Paths(dir);
        _configurationStub.Answer("get_CommonApplicationPaths", _ => paths);
        _configurationStub.Answer("GetConfiguration", args => string.Equals((string?)args[0], "network", StringComparison.Ordinal) ? _network : null);
        _configurationStub.Answer("SaveConfiguration", args =>
        {
            OnSave?.Invoke((string)args[0]!, args[1]!);
            return null;
        });
        (System, _systemStub) = InterfaceStub.Create<ISystemManager>();
        _systemStub.Answer("Restart", _ =>
        {
            OnRestart?.Invoke();
            return null;
        });
        var (lifetime, lifetimeStub) = InterfaceStub.Create<IHostApplicationLifetime>();
        Started = started;
        lifetimeStub.Answer("get_ApplicationStarted", _ => new CancellationToken(Started));
        Lifetime = lifetime;
    }

    /// <summary>Gets or sets a value indicating whether Jellyfin has finished starting.</summary>
    public bool Started { get; set; }

    /// <summary>Gets the configuration manager.</summary>
    public IConfigurationManager Configuration { get; }

    /// <summary>Gets the system manager.</summary>
    public ISystemManager System { get; }

    /// <summary>Gets the host lifetime.</summary>
    public IHostApplicationLifetime Lifetime { get; }

    /// <summary>Gets or sets what runs on each configuration save, given its key and value. It may throw.</summary>
    public Action<string, object>? OnSave { get; set; }

    /// <summary>Gets or sets what runs on each restart. It may throw.</summary>
    public Action? OnRestart { get; set; }

    /// <summary>Gets how many times a configuration with the key was saved.</summary>
    /// <param name="key">The configuration key, such as network.</param>
    /// <returns>The count.</returns>
    public int Saves(string key) => _configurationStub.Calls.Count(c => c.Name == "SaveConfiguration" && string.Equals((string?)c.Args[0], key, StringComparison.Ordinal));

    /// <summary>Gets how many restarts were asked for.</summary>
    public int Restarts => _systemStub.Calls.Count(c => c.Name == "Restart");

    /// <summary>
    /// Makes Jellyfin hand out the same network configuration every time, as its configuration manager does.
    /// </summary>
    /// <param name="network">The network configuration.</param>
    public void Serve(object network) => _network = network;
}
