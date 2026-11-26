using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Mvc;
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
/// What the block list and the proxy settings refuse or tidy up before anything is stored.
/// </summary>
[Collection(PluginInstanceCollection.Name)]
public class BlockGuardTests
{

    [Fact]
    public void BlockList_AnIPv4MappedEntry_NamesTheIPv4Address()
    {
        Assert.True(BlockGuard.TryParseTarget("203.0.113.5", out var target));
        Assert.True(BlockGuard.NamesOnly("::ffff:203.0.113.5", target));
        Assert.True(BlockGuard.NamesOnly("203.0.113.5/32", target));
        Assert.False(BlockGuard.NamesOnly("203.0.113.0/24", target));
    }

    [Fact]
    public void Validator_NullListEntries_AreDropped()
    {
        var s = new ProxySettings();
        s.Advanced.TrustedNetworks = new List<string> { null!, "10.0.0.0/8" };
        s.Advanced.BlockedAddresses = new List<string> { null!, "203.0.113.5" };
        ProxySettingsValidator.Normalize(s);
        Assert.Equal(new[] { "10.0.0.0/8" }, s.Advanced.TrustedNetworks);
        Assert.Equal(new[] { "203.0.113.5" }, s.Advanced.BlockedAddresses);
    }
}
