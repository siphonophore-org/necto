using System;
using System.Security.Claims;
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

namespace Jellyfin.Plugin.Necto.Tests.Api.Ddns;

/// <summary>
/// Who an activity entry for an administrator's change names: the signed-in user, or "An administrator" when
/// the request carries no user, as one made with an API key.
/// </summary>
public sealed class ActivityUserTests
{
    [Fact]
    public void NameOf_SignedInUser_IsTheirName()
    {
        var id = Guid.NewGuid();
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "viewer"),
            new Claim("Jellyfin-UserId", id.ToString("N"))
        }));

        Assert.Equal("viewer", ActivityUser.NameOf(user));
        Assert.Equal(id, ActivityUser.Of(user));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void NameOf_NoName_IsAnAdministrator(string? name)
    {
        var user = name is null
            ? new ClaimsPrincipal(new ClaimsIdentity())
            : new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, name) }));

        Assert.Equal("An administrator", ActivityUser.NameOf(user));
        Assert.Equal("An administrator", ActivityUser.NameOf(null));
    }
}
