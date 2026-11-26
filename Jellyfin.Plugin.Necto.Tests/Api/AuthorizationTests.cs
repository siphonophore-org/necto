using System;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.Necto.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Api;

/// <summary>
/// Every dashboard controller asks for an administrator, and the pages' strings are the only route open to anyone. The
/// user pages' controller signs every call in, and serves only its page's own files to anyone.
/// </summary>
public class AuthorizationTests
{
    // The controllers behind user pages, which signed in users who are not administrators call.
    private static readonly Type[] UserPageControllers = { typeof(Jellyfin.Plugin.Necto.Api.Requests.RequestsController) };

    private static readonly Type[] AllControllers = typeof(Plugin).Assembly.GetTypes().Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract).ToArray();

    private static readonly Type[] Controllers = AllControllers.Except(UserPageControllers).ToArray();

    /// <summary>
    /// Every route of a user page's controller signs the caller in, except the page's own files, which carry no data.
    /// True: every call that reads or changes data needs a Jellyfin sign in.
    /// False: a route would hand out requests or issues to anyone.
    /// </summary>
    [Fact]
    public void UserPageRoutes_SignInEveryDataCall()
    {
        foreach (var controller in UserPageControllers)
        {
            var actions = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
                .ToList();
            Assert.NotEmpty(actions);
            foreach (var action in actions)
            {
                var anonymous = action.GetCustomAttribute<AllowAnonymousAttribute>() is not null;
                var template = action.GetCustomAttributes<HttpMethodAttribute>().First().Template ?? string.Empty;
                if (anonymous)
                {
                    // Anonymous routes are the page and its files under /requests and /issues, never an API route.
                    Assert.False(template.StartsWith('/'), action.Name + " is an API route open to anyone.");
                    Assert.All(action.GetCustomAttributes<HttpMethodAttribute>(), a => Assert.Contains("GET", a.HttpMethods));
                }
                else
                {
                    Assert.NotNull(action.GetCustomAttribute<AuthorizeAttribute>());
                }
            }
        }
    }

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
    /// Only the strings route allows anonymous callers.
    /// True: nothing but the page's words is served without a sign in.
    /// False: a route would answer anyone.
    /// </summary>
    [Fact]
    public void OnlyTranslations_AllowsAnonymous()
    {
        var open = Controllers
            .SelectMany(c => c.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
            .ToList();

        Assert.Equal(new[] { typeof(NectoController).GetMethod(nameof(NectoController.Translations)) }, open);
        Assert.All(AllControllers, c => Assert.Null(c.GetCustomAttribute<AllowAnonymousAttribute>()));
    }

    /// <summary>
    /// No route starts with a segment Jellyfin 12.4 answers at.
    /// True: Necto's routes never collide with Jellyfin's own.
    /// False: two answers on one route would break both.
    /// </summary>
    [Fact]
    public void Routes_AvoidJellyfinsOwnSegments()
    {
        var jellyfin = new[] { "Users", "Sessions", "Devices", "System", "Plugins", "ScheduledTasks", "Library", "Items", "Auth", "SyncPlay", "LiveTv", "Startup", "Branding", "Playlists" };

        foreach (var controller in AllControllers)
        {
            // A route that starts with a slash leaves the controller's own, so its first segment counts as well.
            var templates = controller.GetCustomAttributes<RouteAttribute>().Select(r => r.Template)
                .Concat(controller.GetMethods().SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>()).Select(a => a.Template ?? string.Empty).Where(t => t.StartsWith('/')));
            foreach (var template in templates)
            {
                var first = template.TrimStart('/').Split('/')[0];
                Assert.DoesNotContain(first, jellyfin, StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}
