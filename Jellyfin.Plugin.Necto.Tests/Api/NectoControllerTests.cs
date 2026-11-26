using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.Necto.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Api;

/// <summary>
/// The Necto routes answer at /Necto, for administrators, with the strings open to anyone.
/// </summary>
public class NectoControllerTests
{
    /// <summary>
    /// The controller answers at Necto and asks for an administrator.
    /// True: the page reaches its routes, and only an administrator reaches any that are not marked open.
    /// False: the page loads its strings from a route that does not exist, or a route is open to every user.
    /// </summary>
    [Fact]
    public void Controller_RoutesAtNecto_ForAdministrators()
    {
        var type = typeof(NectoController);

        Assert.Equal("Necto", type.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal("RequiresElevation", type.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
    }

    /// <summary>
    /// Translations answers GET Necto/Translations to anyone.
    /// True: the page gets its strings at the address necto_shared.js asks, before or without a sign in.
    /// False: the page shows its English fallbacks, or a key in place of every string.
    /// </summary>
    [Fact]
    public void Translations_IsAnAnonymousGet()
    {
        var method = typeof(NectoController).GetMethod(nameof(NectoController.Translations))!;

        var get = Assert.Single(method.GetCustomAttributes<HttpGetAttribute>());
        Assert.Equal("Translations", get.Template);
        Assert.NotNull(method.GetCustomAttribute<AllowAnonymousAttribute>());
    }
}
