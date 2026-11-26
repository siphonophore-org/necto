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

namespace Jellyfin.Plugin.Necto.Tests.Api.Proxy;

/// <summary>
/// The Dashboard's share card: the public address, which the page draws as a QR code.
/// </summary>
public class ShareTests
{
    [Fact]
    public void Share_AnAddress_GivesTheAddress()
    {
        var info = ShareController.Create("https://media.example.com");
        Assert.Equal("https://media.example.com", info.Url);
        Assert.False(info.HomeOnly);
    }

    [Fact]
    public void Share_NoAddress_IsEmpty()
    {
        var info = ShareController.Create(string.Empty);
        Assert.Empty(info.Url);
    }
}
