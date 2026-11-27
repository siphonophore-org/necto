using System;
using System.Net.Http;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Activity;
using Microsoft.Extensions.Logging.Abstractions;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Tests.Fakes;

/// <summary>
/// Stand-ins the DDNS and Proxy tests build often: application paths over a folder, an activity logger that hands
/// each entry to the test, and HTTP client factories.
/// </summary>
public static class Stubs
{
    /// <summary>
    /// Builds application paths whose configuration, plugin, data and cache folders are all one folder.
    /// </summary>
    /// <param name="dir">The folder.</param>
    /// <returns>The paths.</returns>
    public static IApplicationPaths Paths(string dir)
    {
        var (paths, stub) = InterfaceStub.Create<IApplicationPaths>();
        stub.Answer("get_PluginConfigurationsPath", _ => dir);
        stub.Answer("get_PluginsPath", _ => dir);
        stub.Answer("get_DataPath", _ => dir);
        stub.Answer("get_CachePath", _ => dir);
        stub.Answer("get_ConfigurationDirectoryPath", _ => dir);
        stub.Answer("get_ProgramDataPath", _ => dir);
        return paths;
    }

    /// <summary>
    /// Builds an activity logger that hands each entry it writes to the test.
    /// </summary>
    /// <param name="add">Takes each entry.</param>
    /// <returns>The logger.</returns>
    public static ActivityLogger Activity(Action<ActivityLog> add)
    {
        ArgumentNullException.ThrowIfNull(add);
        var (manager, stub) = InterfaceStub.Create<IActivityManager>();
        stub.Answer("CreateAsync", args =>
        {
            add((ActivityLog)args[0]!);
            return Task.CompletedTask;
        });
        return new ActivityLogger(manager, NullLogger<ActivityLogger>.Instance);
    }

    /// <summary>
    /// Builds a client factory that hands out the clients a test makes.
    /// </summary>
    /// <param name="client">Makes each client.</param>
    /// <returns>The factory.</returns>
    public static FuncHttpFactory Http(Func<HttpClient> client) => new(client);

    /// <summary>
    /// Builds a client factory that fails the test if anything asks it for a client.
    /// </summary>
    /// <returns>The factory.</returns>
    public static FuncHttpFactory NoHttp() => new(() => throw new InvalidOperationException("attempted to create an HTTP client"));
}

/// <summary>
/// A client factory over a function, which counts how many clients were asked for.
/// </summary>
public sealed class FuncHttpFactory : IHttpClientFactory
{
    private readonly Func<HttpClient> _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="FuncHttpFactory"/> class.
    /// </summary>
    /// <param name="client">Makes each client.</param>
    public FuncHttpFactory(Func<HttpClient> client) => _client = client;

    /// <summary>Gets how many clients were asked for.</summary>
    public int Created { get; private set; }

    /// <inheritdoc />
    public HttpClient CreateClient(string name)
    {
        Created++;
        return _client();
    }
}
