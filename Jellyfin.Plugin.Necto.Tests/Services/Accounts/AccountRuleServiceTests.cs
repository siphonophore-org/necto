using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Configuration.Accounts;
using Jellyfin.Plugin.Necto.Services.Accounts;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Accounts;

/// <summary>
/// The account rules disable or delete users whose groups have all expired, disable users inactive past every group's
/// window, leave users who never signed in alone, and never touch an administrator.
/// </summary>
public class AccountRuleServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeUsers _users = new();
    private readonly FakeConfigurationStore _config = new();
    private readonly FakeActivity _activity = new();

    private Task<AccountRuleRun> Run()
        => new AccountRuleService(_config, _users.Manager, _activity.Logger, FakeServerConfig.Create(), NullLogger<AccountRuleService>.Instance, new FixedTime(Now))
            .ProcessAsync(null, CancellationToken.None);

    private void Inactive(int days, params Guid[] members)
        => _config.Configuration.Groups.Add(TestGroups.Make("Quiet", p => { p.Accounts.Inactivity.Enabled = true; p.Accounts.Inactivity.Days = days; }, members));

    private void Expires(int daysFromNow, ExpiryAction action, params Guid[] members)
        => _config.Configuration.Groups.Add(TestGroups.Make("Trial", p => { p.Accounts.Expiry.ExpiresOn = Now.UtcDateTime.Date.AddDays(daysFromNow); p.Accounts.Expiry.Action = action; }, members));

    /// <summary>
    /// A member inactive past the window is disabled, and the run writes one entry.
    /// True: an account no one uses stops working on its own.
    /// False: the rule would never disable anyone.
    /// </summary>
    [Fact]
    public async Task Process_InactiveMember_IsDisabled()
    {
        var viewer = _users.Add("viewer", lastActivity: Now.UtcDateTime.AddDays(-40));
        Inactive(30, viewer.Id);

        var run = await Run();

        Assert.True(_users.IsDisabled(viewer.Id));
        Assert.Equal(1, run.Disabled);
        Assert.Contains("User disabled", _activity.Names);
    }

    /// <summary>
    /// A recently active member and one who never signed in are left alone.
    /// True: only a user who stopped using the server is disabled.
    /// False: a new account would be disabled before its first sign in.
    /// </summary>
    [Fact]
    public async Task Process_RecentOrNeverSignedIn_IsLeftAlone()
    {
        var recent = _users.Add("recent", lastActivity: Now.UtcDateTime.AddDays(-2));
        var fresh = _users.Add("fresh");
        Inactive(30, recent.Id, fresh.Id);

        await Run();

        Assert.False(_users.IsDisabled(recent.Id));
        Assert.False(_users.IsDisabled(fresh.Id));
        Assert.Empty(_activity.Names);
    }

    /// <summary>
    /// A user stored in a second group follows only the first, since a user is in one group.
    /// True: the first group's inactivity rule disables the user.
    /// False: a stored second group would still keep the user enabled.
    /// </summary>
    [Fact]
    public async Task Process_UserStoredInASecondGroup_FollowsTheFirst()
    {
        var viewer = _users.Add("viewer", lastActivity: Now.UtcDateTime.AddDays(-400));
        Inactive(30, viewer.Id);
        _config.Configuration.Groups.Add(TestGroups.Make("Family", viewer.Id));

        await Run();

        Assert.True(_users.IsDisabled(viewer.Id));
    }

    /// <summary>
    /// An expired group with Delete deletes its member and drops them from every group.
    /// True: a trial account goes away on its date.
    /// False: the account would stay, or a deleted id would stay a member.
    /// </summary>
    [Fact]
    public async Task Process_ExpiredDelete_DeletesTheMember()
    {
        var viewer = _users.Add("viewer");
        Expires(-1, ExpiryAction.Delete, viewer.Id);

        var run = await Run();

        Assert.Contains(viewer.Id, _users.Deleted);
        Assert.Equal(1, run.Deleted);
        Assert.Empty(_config.Configuration.Groups[0].MemberIds);
        Assert.Contains("User deleted", _activity.Names);
    }

    /// <summary>
    /// An expired group with Disable disables its members, but never an administrator.
    /// True: expiry ends members' access and leaves administrators alone.
    /// False: an administrator listed by hand could be locked out.
    /// </summary>
    [Fact]
    public async Task Process_ExpiredDisable_DisablesMembersButNotAdministrators()
    {
        var viewer = _users.Add("viewer");
        var admin = _users.Add("admin", admin: true);
        Expires(0, ExpiryAction.Disable, viewer.Id, admin.Id);

        await Run();

        Assert.True(_users.IsDisabled(viewer.Id));
        Assert.False(_users.IsDisabled(admin.Id));
        Assert.Empty(_users.Deleted);
    }

    /// <summary>
    /// A group that has not expired keeps its member's account.
    /// True: a member expires only on the group's date.
    /// False: an account would end early.
    /// </summary>
    [Fact]
    public async Task Process_GroupNotExpired_KeepsTheAccount()
    {
        var future = _users.Add("future");
        Expires(2, ExpiryAction.Delete, future.Id);

        await Run();

        Assert.Empty(_users.Deleted);
        Assert.False(_users.IsDisabled(future.Id));
    }

    /// <summary>
    /// Everyone's expiry applies to users in no group.
    /// True: rules on Everyone reach every ungrouped user.
    /// False: ungrouped users would escape Everyone's rules.
    /// </summary>
    [Fact]
    public async Task Process_EveryoneExpiry_AppliesToUngroupedUsers()
    {
        var loose = _users.Add("loose");
        var member = _users.Add("member");
        _config.Configuration.Groups.Add(TestGroups.Make("Family", member.Id));
        _config.Configuration.Everyone.Permissions.Accounts.Expiry.ExpiresOn = Now.UtcDateTime.Date.AddDays(-1);

        await Run();

        Assert.True(_users.IsDisabled(loose.Id));
        Assert.False(_users.IsDisabled(member.Id));
    }
}
