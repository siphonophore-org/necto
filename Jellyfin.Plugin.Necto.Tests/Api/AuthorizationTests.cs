using System;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.Necto.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Api;

/// <summary>
/// Every controller asks for an administrator, and the pages' strings are the only route open to anyone.
/// </summary>
public class AuthorizationTests
{
    private static readonly Type[] Controllers = typeof(Plugin).Assembly.GetTypes().Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract).ToArray();

    /// <summary>
    /// Every controller requires elevation.
    /// True: a caller without an administrator's token is refused on every route.
    /// False: a route would be reachable by any user.
    /// </summary>
    [Fact]
    public void EveryController_RequiresElevation()
    {
        Assert.NotEmpty(Controllers);
        Assert.All(Controllers, c => Assert.Contains(c.GetCustomAttributes<AuthorizeAttribute>(true), a => string.Equals(a.Policy, "RequiresElevation", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Only the strings, the shared service worker, and the invite signup routes allow anonymous callers.
    /// True: nothing but the page's words, the worker, and an invite's own signup is served without a sign in.
    /// False: a route would answer anyone.
    /// </summary>
    [Fact]
    public void OnlyPublicRoutes_AllowAnonymous()
    {
        var open = Controllers
            .SelectMany(c => c.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
            .ToList();

        var expected = new[]
        {
            typeof(NectoController).GetMethod(nameof(NectoController.Translations)),
            typeof(Jellyfin.Plugin.Necto.Api.Notifications.NotificationsController).GetMethod(nameof(Jellyfin.Plugin.Necto.Api.Notifications.NotificationsController.Worker))
        }.Concat(PublicInviteRoutes()).ToList();

        Assert.Equal(expected.Select(m => m!.DeclaringType!.Name + "." + m.Name).OrderBy(n => n, StringComparer.Ordinal), open.Select(m => m.DeclaringType!.Name + "." + m.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.All(Controllers, c => Assert.Null(c.GetCustomAttribute<AllowAnonymousAttribute>()));
    }

    private static MethodInfo?[] PublicInviteRoutes() => System.Array.Empty<MethodInfo?>();

    /// <summary>
    /// No route starts with a segment Jellyfin 12.4 answers at.
    /// True: Necto's routes never collide with Jellyfin's own.
    /// False: two answers on one route would break both.
    /// </summary>
    [Fact]
    public void Routes_AvoidJellyfinsOwnSegments()
    {
        var jellyfin = new[] { "Users", "Sessions", "Devices", "System", "Plugins", "ScheduledTasks", "Library", "Items", "Auth", "SyncPlay", "LiveTv", "Startup", "Branding", "Playlists" };

        foreach (var controller in Controllers)
        {
            var first = controller.GetCustomAttribute<RouteAttribute>()!.Template.Split('/')[0];
            Assert.DoesNotContain(first, jellyfin, StringComparer.OrdinalIgnoreCase);
        }
    }
}
