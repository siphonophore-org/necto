using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Api;
using Jellyfin.Plugin.Necto.Api.Groups;
using Jellyfin.Plugin.Necto.Configuration.Groups;
using Jellyfin.Plugin.Necto.Services.Accounts;
using Jellyfin.Plugin.Necto.Services.Groups;
using Jellyfin.Plugin.Necto.Services.Policies;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using MediaBrowser.Common.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Api.Groups;

/// <summary>
/// The Groups routes answer at /Groups for administrators, each at the verb and template the page calls, with the
/// status codes and bodies the page reads.
/// </summary>
public class GroupsControllerTests
{
    private readonly FakeUsers _users = new();
    private readonly FakeConfigurationStore _config = new();

    private GroupsController Controller()
    {
        var activity = new FakeActivity();
        var server = FakeServerConfig.Create();
        var (paths, stub) = InterfaceStub.Create<IApplicationPaths>();
        stub.Answer("get_ProgramDataPath", _ => System.IO.Path.GetTempPath());
        var controller = new GroupsController(
            new GroupStore(_config, _users.Manager, activity.Logger, server, NullLogger<GroupStore>.Instance),
            new PermissionService(_config, _users.Manager),
            new PasswordEnrollment(_config, _users.Manager, new AccountActivity(activity.Logger, server), NullLogger<PasswordEnrollment>.Instance),
            new ResetCodeService(paths, NullLogger<ResetCodeService>.Instance),
            _config,
            _users.Manager,
            new PolicyTemplateService(_config, _users.Manager, NullLogger<PolicyTemplateService>.Instance));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    private static MethodInfo Action(string name) => typeof(GroupsController).GetMethod(name)!;

    /// <summary>
    /// The controller answers at Groups and asks for an administrator.
    /// True: only an administrator reaches any group route.
    /// False: a user could read or change who may do what.
    /// </summary>
    [Fact]
    public void Controller_RoutesAtGroups_ForAdministrators()
    {
        var type = typeof(GroupsController);

        Assert.Equal("Groups", type.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal("RequiresElevation", type.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.DoesNotContain(type.GetMethods(), m => m.GetCustomAttribute<AllowAnonymousAttribute>() is not null);
    }

    /// <summary>
    /// Each action answers at the verb and template the page calls.
    /// True: the page's requests reach their actions.
    /// False: a request would land on another action or on nothing.
    /// </summary>
    [Theory]
    [InlineData(nameof(GroupsController.ListGroups), "GET", null)]
    [InlineData(nameof(GroupsController.GetGroup), "GET", "{id:guid}")]
    [InlineData(nameof(GroupsController.CreateGroup), "POST", null)]
    [InlineData(nameof(GroupsController.UpdateGroup), "PUT", "{id:guid}")]
    [InlineData(nameof(GroupsController.DeleteGroup), "DELETE", "{id:guid}")]
    [InlineData(nameof(GroupsController.ListUsers), "GET", "Users")]
    [InlineData(nameof(GroupsController.GetUserPermissions), "GET", "Users/{userId:guid}/Permissions")]
    [InlineData(nameof(GroupsController.ApplyPasswordRules), "POST", "PasswordRules/Apply")]
    [InlineData(nameof(GroupsController.ListResetCodes), "GET", "ResetCodes")]
    public void Action_AnswersAtItsVerbAndTemplate(string name, string verb, string? template)
    {
        var attribute = Assert.Single(Action(name).GetCustomAttributes<HttpMethodAttribute>());

        Assert.Equal(new[] { verb }, attribute.HttpMethods);
        Assert.Equal(template, attribute.Template);
    }

    /// <summary>
    /// Creating a group answers with the saved group, and a refused one with 400 and the reason.
    /// True: the page opens what it saved, and shows why a save was refused.
    /// False: the page would show a generic error for a taken name.
    /// </summary>
    [Fact]
    public async Task CreateGroup_AnswersTheGroupOrTheReason()
    {
        var created = await Controller().CreateGroup(new Group { Name = "Family" });
        var refused = await Controller().CreateGroup(new Group { Name = "family" });

        var group = Assert.IsType<Group>(Assert.IsType<OkObjectResult>(created.Result).Value);
        Assert.Equal("Family", group.Name);
        var error = Assert.IsType<ApiError>(Assert.IsType<BadRequestObjectResult>(refused.Result).Value);
        Assert.Equal("Another group already has this name.", error.Error);
    }

    /// <summary>
    /// Saving a group moves its members onto its password rules.
    /// True: turning on rules holds members to them at once.
    /// False: rules would wait for the next server start.
    /// </summary>
    [Fact]
    public async Task UpdateGroup_EnrollsMembersInItsRules()
    {
        var viewer = _users.Add("viewer");
        var id = Assert.IsType<Group>(Assert.IsType<OkObjectResult>((await Controller().CreateGroup(new Group { Name = "Family" })).Result).Value).Id;
        var next = new Group { Name = "Family", MemberIds = { viewer.Id } };
        next.Permissions.Accounts.Password.Enabled = true;

        Assert.IsType<OkObjectResult>((await Controller().UpdateGroup(id, next)).Result);

        Assert.Equal(PasswordEnrollment.ProviderId, _users.Users[viewer.Id].AuthenticationProviderId);
        Assert.IsType<NotFoundObjectResult>((await Controller().UpdateGroup(Guid.NewGuid(), next)).Result);
    }

    /// <summary>
    /// Deleting answers 204, Everyone 400, and an unknown group 404.
    /// True: the page tells a deleted group from a refused delete.
    /// False: Everyone could be deleted, or a stale delete would look done.
    /// </summary>
    [Fact]
    public async Task DeleteGroup_AnswersByOutcome()
    {
        var id = Assert.IsType<Group>(Assert.IsType<OkObjectResult>((await Controller().CreateGroup(new Group { Name = "Family" })).Result).Value).Id;

        Assert.IsType<NoContentResult>(await Controller().DeleteGroup(id));
        Assert.IsType<BadRequestObjectResult>(await Controller().DeleteGroup(Group.EveryoneId));
        Assert.IsType<NotFoundObjectResult>(await Controller().DeleteGroup(id));
    }

    /// <summary>
    /// The user list gives each user's groups, Everyone for a user in none, and none for an administrator.
    /// True: the page shows where each user's permissions come from.
    /// False: an administrator would read as held by Everyone.
    /// </summary>
    [Fact]
    public async Task ListUsers_GivesEachUsersGroups()
    {
        var member = _users.Add("member");
        _users.Add("loose");
        _users.Add("admin", admin: true);
        var family = Assert.IsType<Group>(Assert.IsType<OkObjectResult>((await Controller().CreateGroup(new Group { Name = "Family", MemberIds = { member.Id } })).Result).Value);

        var users = Assert.IsAssignableFrom<IReadOnlyList<GroupUser>>(Assert.IsType<OkObjectResult>(Controller().ListUsers().Result).Value);

        Assert.Equal(new[] { "admin", "loose", "member" }, users.Select(u => u.Name));
        Assert.Empty(users[0].GroupIds);
        Assert.True(users[0].IsAdministrator);
        Assert.Equal(new[] { Group.EveryoneId }, users[1].GroupIds);
        Assert.Equal(new[] { family.Id }, users[2].GroupIds);
    }

    /// <summary>
    /// The user list gives each member the expiry date, inactivity window, and last activity their groups give.
    /// True: the Groups page counts the inactive and soon expiring users as the account rules task sees them.
    /// False: the count cards would miss users or count administrators.
    /// </summary>
    [Fact]
    public async Task ListUsers_GivesTheAccountRulesOfEachUser()
    {
        var seen = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var member = _users.Add("member", lastActivity: seen);
        var admin = _users.Add("admin", admin: true, lastActivity: seen);
        var expires = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);
        var rules = new Group { Name = "Family", MemberIds = { member.Id } };
        rules.Permissions.Accounts.Expiry.ExpiresOn = expires;
        rules.Permissions.Accounts.Inactivity.Enabled = true;
        rules.Permissions.Accounts.Inactivity.Days = 30;
        await Controller().CreateGroup(rules);

        var users = Assert.IsAssignableFrom<IReadOnlyList<GroupUser>>(Assert.IsType<OkObjectResult>(Controller().ListUsers().Result).Value);

        var row = users.First(u => u.Id == member.Id);
        Assert.Equal((expires, 30, seen), (row.ExpiresOn!.Value, row.InactiveDays!.Value, row.LastActivityDate!.Value));
        var adminRow = users.First(u => u.Id == admin.Id);
        Assert.Null(adminRow.ExpiresOn);
        Assert.Null(adminRow.InactiveDays);
    }

    /// <summary>
    /// A user's permissions answer with the merge, and an unknown user 404.
    /// True: the page shows what a user may do in each tool.
    /// False: an unknown id would read as Everyone.
    /// </summary>
    [Fact]
    public void GetUserPermissions_AnswersTheMergeOrNotFound()
    {
        var viewer = _users.Add("viewer");
        _config.Configuration.Everyone.Permissions.Issues.MayReport = true;

        var found = Assert.IsType<EffectivePermissions>(Assert.IsType<OkObjectResult>(Controller().GetUserPermissions(viewer.Id).Result).Value);

        Assert.True(found.Issues.MayReport);
        Assert.IsType<NotFoundObjectResult>(Controller().GetUserPermissions(Guid.NewGuid()).Result);
    }

    /// <summary>
    /// Reset codes are listed only while Settings shows them.
    /// True: codes never leave the server unless an administrator turned them on.
    /// False: every administrator page load would carry the codes.
    /// </summary>
    [Fact]
    public void ListResetCodes_OnlyWhileShown()
    {
        var off = Assert.IsType<ResetCodes>(Assert.IsType<OkObjectResult>(Controller().ListResetCodes().Result).Value);
        _config.Configuration.AccountSettings.ShowResetCodes = true;
        var on = Assert.IsType<ResetCodes>(Assert.IsType<OkObjectResult>(Controller().ListResetCodes().Result).Value);

        Assert.False(off.Enabled);
        Assert.Empty(off.Codes);
        Assert.True(on.Enabled);
    }

    /// <summary>
    /// Applying the password rules answers how many users moved.
    /// True: Troubleshooting reports what the repair did.
    /// False: the page could not tell a repair from nothing.
    /// </summary>
    [Fact]
    public async Task ApplyPasswordRules_AnswersHowManyMoved()
    {
        _users.Add("viewer");
        _config.Configuration.Everyone.Permissions.Accounts.Password.Enabled = true;

        var applied = Assert.IsType<PasswordRulesApplied>(Assert.IsType<OkObjectResult>((await Controller().ApplyPasswordRules(CancellationToken.None)).Result).Value);

        Assert.Equal(1, applied.Moved);
    }
}
