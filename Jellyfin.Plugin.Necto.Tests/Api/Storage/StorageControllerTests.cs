using System;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.Necto.Api.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Api.Storage;

/// <summary>
/// The Storage routes sit under /Storage, each named for what it does, and every one is for administrators.
/// </summary>
public class StorageControllerTests
{
    /// <summary>
    /// The controller answers at /Storage and asks for an administrator.
    /// True: only an administrator can list, format, or make folders on disks.
    /// False: any user could erase a disk.
    /// </summary>
    [Fact]
    public void Controller_IsUnderStorage_ForAdministrators()
    {
        var type = typeof(StorageController);

        Assert.Equal("Storage", type.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Contains(type.GetCustomAttributes<AuthorizeAttribute>(), a => a.Policy == "RequiresElevation");
        Assert.DoesNotContain(type.GetMethods(), m => m.GetCustomAttribute<AllowAnonymousAttribute>() is not null);
    }

    /// <summary>
    /// The routes are the disks, the format options and format, and new folders, with no strings route of their own.
    /// True: the page reaches each action at the route the docs name.
    /// False: a page call would miss its route.
    /// </summary>
    [Fact]
    public void Routes_NameWhatTheyDo()
    {
        var routes = typeof(StorageController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>().Select(a => a.HttpMethods.Single() + " " + a.Template))
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "GET Disks", "GET Format", "POST Folders", "POST Format" }, routes);
    }
}
