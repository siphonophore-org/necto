using System;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Api;
using Jellyfin.Plugin.Necto.Api.Groups;
using Jellyfin.Plugin.Necto.Api.Invites;
using Jellyfin.Plugin.Necto.Api.Notifications;
using Jellyfin.Plugin.Necto.Services.Invites;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Api;

/// <summary>
/// The Notifications, Invites, and group order routes answer at the verbs and templates the pages call, with the
/// access each needs: administrators for the tools, any signed in user for their own browser and invites, and anyone
/// for an invite's signup.
/// </summary>
public sealed class RouteTests : IDisposable
{
    private readonly InviteHarness _h = new();

    public void Dispose() => _h.Dispose();

    private static HttpMethodAttribute Verb(Type controller, string action) => Assert.Single(controller.GetMethod(action)!.GetCustomAttributes<HttpMethodAttribute>());

    private static string? Access(Type controller, string action)
    {
        var method = controller.GetMethod(action)!;
        if (method.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
        {
            return "anyone";
        }

        // A class that asks for elevation asks it of every action, whatever the action adds.
        if (controller.GetCustomAttributes<AuthorizeAttribute>(true).Any(a => string.Equals(a.Policy, "RequiresElevation", StringComparison.Ordinal)))
        {
            return "administrator";
        }

        return method.GetCustomAttribute<AuthorizeAttribute>() is not null || controller.GetCustomAttribute<AuthorizeAttribute>() is not null ? "user" : "administrator";
    }

    /// <summary>
    /// Each Notifications action answers at its verb, template, and access.
    /// True: the pages reach their routes, and a user reaches only their own browser routes and the worker.
    /// False: a user could read the channels, or a page would call a route that is not there.
    /// </summary>
    [Theory]
    [InlineData(nameof(NotificationsController.Events), "GET", "Events", "administrator")]
    [InlineData(nameof(NotificationsController.Channels), "GET", "Channels", "administrator")]
    [InlineData(nameof(NotificationsController.Test), "POST", "Channels/{id}/Test", "administrator")]
    [InlineData(nameof(NotificationsController.Preview), "POST", "Preview", "administrator")]
    [InlineData(nameof(NotificationsController.GetSettings), "GET", "Settings", "administrator")]
    [InlineData(nameof(NotificationsController.SaveSettings), "PUT", "Settings", "administrator")]
    [InlineData(nameof(NotificationsController.Log), "GET", "Log", "administrator")]
    [InlineData(nameof(NotificationsController.ClearLog), "DELETE", "Log", "administrator")]
    [InlineData(nameof(NotificationsController.Push), "GET", "Push", "administrator")]
    [InlineData(nameof(NotificationsController.ResetKeys), "POST", "Push/Keys", "administrator")]
    [InlineData(nameof(NotificationsController.ClearHeld), "DELETE", "Held", "administrator")]
    [InlineData(nameof(NotificationsController.Worker), "GET", "Push/Worker.js", "anyone")]
    public void Notifications_AnswerAtTheirRoutes(string action, string verb, string template, string access)
    {
        var attribute = Verb(typeof(NotificationsController), action);

        Assert.Equal("Notifications", typeof(NotificationsController).GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal(new[] { verb }, attribute.HttpMethods);
        Assert.Equal(template, attribute.Template);
        Assert.Equal(access, Access(typeof(NotificationsController), action));
    }

    /// <summary>
    /// Each Invites action answers at its verb, template, and access, and the user pages sit at /invites.
    /// True: the tool, a member's page, and the signup page each reach their routes, with none at /Invites alone.
    /// False: the list would collide with the member page, or a member could change another's invites.
    /// </summary>
    [Theory]
    [InlineData(nameof(InvitesController.List), "GET", "List", "administrator")]
    [InlineData(nameof(InvitesController.All), "GET", "All", "administrator")]
    [InlineData(nameof(InvitesController.Create), "POST", null, "administrator")]
    [InlineData(nameof(InvitesController.Update), "PUT", "{id:guid}", "administrator")]
    [InlineData(nameof(InvitesController.Duplicate), "POST", "{id:guid}/Duplicate", "administrator")]
    [InlineData(nameof(InvitesController.Delete), "DELETE", "{id:guid}", "administrator")]
    [InlineData(nameof(InvitesController.SetEnabled), "POST", "{id:guid}/Enabled", "administrator")]
    [InlineData(nameof(InvitesController.SetExpiry), "POST", "{id:guid}/Expiry", "administrator")]
    [InlineData(nameof(InvitesController.MemberInvites), "GET", "/invites", "anyone")]
    [InlineData(nameof(InvitesController.Signup), "GET", "/invites/{token}", "anyone")]
    [InlineData(nameof(InvitesController.Info), "GET", "/invites/{token}/Info", "anyone")]
    [InlineData(nameof(InvitesController.Redeem), "POST", "/invites/{token}/Redeem", "anyone")]
    public void Invites_AnswerAtTheirRoutes(string action, string verb, string? template, string access)
    {
        var attribute = Verb(typeof(InvitesController), action);

        Assert.Equal("Invites", typeof(InvitesController).GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal(new[] { verb }, attribute.HttpMethods);
        Assert.Equal(template, attribute.Template);
        Assert.Equal(access, Access(typeof(InvitesController), action));
    }

    /// <summary>
    /// The group order and template routes answer under Groups for administrators.
    /// True: the Groups page reorders and repairs at its own routes.
    /// False: a user could reorder the groups.
    /// </summary>
    [Theory]
    [InlineData(nameof(GroupTemplatesController.ApplyTemplates), "POST", "Templates/Apply")]
    public void GroupOrder_AnswersAtItsRoutes(string action, string verb, string template)
    {
        var attribute = Verb(typeof(GroupTemplatesController), action);

        Assert.Equal("Groups", typeof(GroupTemplatesController).GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal(new[] { verb }, attribute.HttpMethods);
        Assert.Equal(template, attribute.Template);
        Assert.Equal("administrator", Access(typeof(GroupTemplatesController), action));
    }

    private MemberInvitesController MemberInvites(Guid user)
    {
        var controller = new MemberInvitesController(_h.Service, _h.Config, _h.Users.Manager);
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(Caller.UserIdClaim, user.ToString()) }, "test"))
        };
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        return controller;
    }

    private InvitesController Invites(InviteAttemptLimiter? limiter = null, Guid? user = null)
    {
        var controller = new InvitesController(_h.Service, limiter ?? new InviteAttemptLimiter(), _h.Config, _h.Users.Manager, NullLogger<InvitesController>.Instance);
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.5");
        if (user is { } id)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(Caller.UserIdClaim, id.ToString()) }, "test"));
        }

        controller.ControllerContext = new ControllerContext { HttpContext = context };
        return controller;
    }

    /// <summary>
    /// The signup's info says whether the invite works and whether it asks for a PIN, and never names it.
    /// True: the signup page shows the form or the unavailable card, and the invite's name stays private.
    /// False: a stranger could read the invite's name, or a dead invite would show the form.
    /// </summary>
    [Fact]
    public void Info_SaysWhetherTheInviteWorks()
    {
        var (invite, _) = _h.Service.Create(new InviteRequest { Label = "Secret name", Message = "Hi", Pin = "123456" }, Guid.Empty, null);

        var good = Assert.IsType<InviteInfoView>(Assert.IsType<OkObjectResult>(Invites().Info(invite!.Token).Result).Value);
        var bad = Assert.IsType<InviteInfoView>(Assert.IsType<OkObjectResult>(Invites().Info("nope").Result).Value);

        Assert.True(good.Valid);
        Assert.True(good.RequiresPin);
        Assert.Equal("Hi", good.Message);
        Assert.False(bad.Valid);
    }

    /// <summary>
    /// Past the limit the signup routes answer 429 with a Retry-After and the page's shape.
    /// True: a flood of guesses is slowed, and the page shows why.
    /// False: an address could guess without end.
    /// </summary>
    [Fact]
    public async Task Redeem_PastTheLimit_Answers429()
    {
        var limiter = new InviteAttemptLimiter();
        for (var i = 0; i < InviteAttemptLimiter.MaxRedeemAttempts; i++)
        {
            await Invites(limiter).Redeem("nope", new RedeemRequest(), default);
        }

        var controller = Invites(limiter);
        var answer = Assert.IsType<ObjectResult>((await controller.Redeem("nope", new RedeemRequest(), default)).Result);

        Assert.Equal(StatusCodes.Status429TooManyRequests, answer.StatusCode);
        Assert.False(Assert.IsType<InviteRedeemResult>(answer.Value).Success);
        Assert.True(controller.Response.Headers.ContainsKey("Retry-After"));
    }

    /// <summary>
    /// A member's page lists only their invites and the groups they may invite to.
    /// True: a member sees their own links and nothing of anyone else's.
    /// False: a member could see the administrators' invites.
    /// </summary>
    [Fact]
    public void Mine_ListsOnlyTheMembersInvites()
    {
        var target = TestGroups.Make("Guests");
        var member = _h.Users.Add("member");
        _h.Config.Configuration.Groups.Add(target);
        _h.Config.Configuration.Groups.Add(TestGroups.Make("Inviters", p => { p.Invites.MayCreate = true; p.Invites.MaxInvites = 0; p.Invites.TargetGroupIds.Add(target.Id); }, member.Id));
        _h.Service.Create(new InviteRequest(), Guid.Empty, null);
        _h.Service.CreateForMember(member.Id, new InviteRequest { GroupId = target.Id }, null);

        var view = Assert.IsType<MemberInvitesView>(Assert.IsType<OkObjectResult>(MemberInvites(member.Id).Mine().Result).Value);

        Assert.True(view.MayCreate);
        Assert.Single(view.Invites);
        Assert.Equal(new[] { target.Id }, view.Groups.Select(g => g.Id));
    }

    /// <summary>
    /// The routes user pages call for their signed in user answer to any user, not only administrators.
    /// True: a member turns on browser notifications and makes their own invites.
    /// False: those pages answer 403 to everyone but administrators.
    /// </summary>
    [Theory]
    [InlineData(typeof(PushController), "Notifications/Push", nameof(PushController.PushKey), "GET", "Key")]
    [InlineData(typeof(PushController), "Notifications/Push", nameof(PushController.Subscribe), "POST", "Subscribe")]
    [InlineData(typeof(PushController), "Notifications/Push", nameof(PushController.Unsubscribe), "POST", "Unsubscribe")]
    [InlineData(typeof(MemberInvitesController), "Invites/Mine", nameof(MemberInvitesController.Mine), "GET", null)]
    [InlineData(typeof(MemberInvitesController), "Invites/Mine", nameof(MemberInvitesController.CreateMine), "POST", null)]
    [InlineData(typeof(MemberInvitesController), "Invites/Mine", nameof(MemberInvitesController.DeleteMine), "DELETE", "{id:guid}")]
    public void UserRoutes_AnswerAnySignedInUser(Type controller, string route, string action, string verb, string? template)
    {
        var attribute = Verb(controller, action);

        Assert.Equal(route, controller.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal(new[] { verb }, attribute.HttpMethods);
        Assert.Equal(template, attribute.Template);
        Assert.Equal("user", Access(controller, action));
    }
}
