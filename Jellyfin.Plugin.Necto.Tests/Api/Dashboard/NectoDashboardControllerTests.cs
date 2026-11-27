using System.Reflection;
using Jellyfin.Plugin.Necto.Api.Dashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Api.Dashboard;

/// <summary>
/// The Dashboard's routes answer at the paths the page calls, for administrators only.
/// </summary>
public class NectoDashboardControllerTests
{
    /// <summary>
    /// Each action answers GET at its template under /Dashboard, and the controller asks for an administrator.
    /// True: the page reaches its map and activity.
    /// False: the page would call a route that is not there, or a user could read the visitor map.
    /// </summary>
    [Theory]
    [InlineData(nameof(NectoDashboardController.Map), "Map")]
    [InlineData(nameof(NectoDashboardController.Activity), "Activity")]
    public void Routes_AnswerAtTheirTemplates(string action, string template)
    {
        var verb = Assert.Single(typeof(NectoDashboardController).GetMethod(action)!.GetCustomAttributes<HttpMethodAttribute>());

        Assert.Equal("Dashboard", typeof(NectoDashboardController).GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal(new[] { "GET" }, verb.HttpMethods);
        Assert.Equal(template, verb.Template);
        Assert.Equal("RequiresElevation", typeof(NectoDashboardController).GetCustomAttribute<AuthorizeAttribute>()!.Policy);
    }
}
