using System.Net;
using Jellyfin.Plugin.Necto.Services.Invites;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Invites;

/// <summary>
/// An address becomes the key the limiter counts, and a proxy style address is recognized.
/// </summary>
public class ClientAddressTests
{
    /// <summary>
    /// IPv6 is keyed by its /64 and IPv4 mapped addresses by their IPv4 form.
    /// True: one home or host has one budget, however it connects.
    /// False: a visitor could get a fresh budget per address.
    /// </summary>
    [Theory]
    [InlineData("2001:db8:1:2:aaaa::1", "2001:db8:1:2::/64")]
    [InlineData("2001:db8:1:2:ffff:ffff:ffff:ffff", "2001:db8:1:2::/64")]
    [InlineData("::ffff:203.0.113.9", "203.0.113.9")]
    [InlineData("203.0.113.9", "203.0.113.9")]
    public void Key_GroupsIPv6ByPrefixAndUnmapsIPv4(string address, string expected)
        => Assert.Equal(expected, ClientAddress.Key(IPAddress.Parse(address)));

    /// <summary>
    /// An unknown address has no key.
    /// True: an unknown caller shares the unknown budget.
    /// False: a missing address would throw.
    /// </summary>
    [Fact]
    public void Key_Null_IsNull() => Assert.Null(ClientAddress.Key(null));

    /// <summary>
    /// Loopback, private, and link local addresses are the kind a proxy connects from.
    /// True: the warning about an untrusted proxy fires only for one.
    /// False: a public visitor would be taken for a proxy.
    /// </summary>
    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.20.0.5", true)]
    [InlineData("192.168.1.10", true)]
    [InlineData("::1", true)]
    [InlineData("fd00::5", true)]
    [InlineData("::ffff:192.168.1.10", true)]
    [InlineData("203.0.113.9", false)]
    [InlineData("172.32.0.1", false)]
    [InlineData("2001:db8::1", false)]
    public void IsPrivate_RecognizesProxyStyleAddresses(string address, bool expected)
        => Assert.Equal(expected, ClientAddress.IsPrivate(IPAddress.Parse(address)));
}
