using System.Net;
using System.Net.Sockets;
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
/// What counts as a public address worth publishing, and which detection replies are taken as addresses.
/// </summary>
public class IPAddressClassifierTests
{
    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("10.0.0.1", false)]
    [InlineData("2606:4700::1111", true)]
    [InlineData("2001:db8::1", false)]
    [InlineData("3fff::1", false)]
    [InlineData("2001::1", false)]
    [InlineData("2001:2::1", false)]
    [InlineData("2001:20::1", false)]
    [InlineData("100::1", false)]
    [InlineData("::10.0.0.1", false)]
    [InlineData("2002:0a00:0001::1", false)]
    [InlineData("2002:0808:0808::1", true)]
    [InlineData("64:ff9b::192.168.1.1", false)]
    [InlineData("64:ff9b::8.8.8.8", true)]
    [InlineData("64:ff9b:1::1", false)]
    [InlineData("::ffff:192.168.1.1", false)]
    public void IsPublic(string address, bool expected)
        => Assert.Equal(expected, IPAddressClassifier.IsPublic(IPAddress.Parse(address)));

    [Theory]
    [InlineData("203.0.113.9", AddressFamily.InterNetwork, true)]
    [InlineData("8.8", AddressFamily.InterNetwork, false)]
    [InlineData("134744072", AddressFamily.InterNetwork, false)]
    [InlineData("0x8.8.8.8", AddressFamily.InterNetwork, false)]
    [InlineData("2606:4700::1111", AddressFamily.InterNetworkV6, true)]
    [InlineData("fe80::1%eth0", AddressFamily.InterNetworkV6, false)]
    [InlineData("", AddressFamily.InterNetworkV6, false)]
    public void DetectionReply_MustBeAPlainAddress(string body, AddressFamily family, bool expected)
        => Assert.Equal(expected, IPDetectionService.IsPlainAddress(body, family));
}
