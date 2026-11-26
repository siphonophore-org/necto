using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.Configuration;

namespace Jellyfin.Plugin.Necto.Tests.Fakes;

/// <summary>
/// Builds a stand-in for the server configuration, in US English.
/// </summary>
public static class FakeServerConfig
{
    public static IServerConfigurationManager Create()
    {
        var (config, stub) = InterfaceStub.Create<IServerConfigurationManager>();
        var server = new ServerConfiguration { UICulture = "en-US" };
        stub.Answer("get_Configuration", _ => server);
        return config;
    }
}
