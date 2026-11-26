using System.Threading.Tasks;
using Jellyfin.Data.Events.Users;
using Jellyfin.Plugin.Necto.Services.Accounts;
using Jellyfin.Plugin.Necto.Services.Groups;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Groups;

/// <summary>
/// A new user joins the group for new users and moves onto its password rules, and a deleted user leaves every group.
/// </summary>
public class UserConsumerTests
{
    private readonly FakeUsers _users = new();
    private readonly FakeConfigurationStore _config = new();

    private UserCreatedConsumer Created() => new(_config, new PasswordEnrollment(_config, _users.Manager, new AccountActivity(new FakeActivity().Logger, FakeServerConfig.Create()), NullLogger<PasswordEnrollment>.Instance), NullLogger<UserCreatedConsumer>.Instance);

    /// <summary>
    /// A new user joins the group for new users and its password rules.
    /// True: a new account starts in the group Settings names, held to its rules.
    /// False: a new user would start in Everyone, or skip the group's password rules.
    /// </summary>
    [Fact]
    public async Task OnCreated_JoinsTheNewUserGroup_AndItsPasswordRules()
    {
        var family = TestGroups.Make("Family", p => p.Accounts.Password.Enabled = true);
        _config.Configuration.Groups.Add(family);
        _config.Configuration.GroupSettings.NewUserGroupId = family.Id;
        var viewer = _users.Add("viewer");

        await Created().OnEvent(new UserCreatedEventArgs(viewer));

        Assert.Equal(new[] { viewer.Id }, family.MemberIds);
        Assert.Equal(PasswordEnrollment.ProviderId, _users.Users[viewer.Id].AuthenticationProviderId);
    }

    /// <summary>
    /// With no group for new users, a new user stays in Everyone.
    /// True: Everyone holds every user no group lists.
    /// False: a new user would be put in a group no one picked.
    /// </summary>
    [Fact]
    public async Task OnCreated_NoNewUserGroup_LeavesTheUserInEveryone()
    {
        var family = TestGroups.Make("Family");
        _config.Configuration.Groups.Add(family);
        var viewer = _users.Add("viewer");

        await Created().OnEvent(new UserCreatedEventArgs(viewer));

        Assert.Empty(family.MemberIds);
        Assert.Equal(FakeUsers.DefaultProvider, _users.Users[viewer.Id].AuthenticationProviderId);
    }

    /// <summary>
    /// A new administrator joins no group.
    /// True: groups never hold an administrator.
    /// False: an administrator would be listed as a member.
    /// </summary>
    [Fact]
    public async Task OnCreated_Administrator_IsLeftAlone()
    {
        var family = TestGroups.Make("Family");
        _config.Configuration.Groups.Add(family);
        _config.Configuration.GroupSettings.NewUserGroupId = family.Id;
        var admin = _users.Add("admin", admin: true);

        await Created().OnEvent(new UserCreatedEventArgs(admin));

        Assert.Empty(family.MemberIds);
    }

    /// <summary>
    /// A deleted user leaves every group and the password rule records.
    /// True: the page stops counting a deleted account at once.
    /// False: a deleted account would count as a member until the next pass.
    /// </summary>
    [Fact]
    public async Task OnDeleted_DropsTheUserEverywhere()
    {
        var viewer = _users.Add("viewer");
        var family = TestGroups.Make("Family", viewer.Id);
        var kids = TestGroups.Make("Kids", viewer.Id);
        _config.Configuration.Groups.AddRange(new[] { family, kids });
        _config.Configuration.ProviderEnrollments.Add(new Jellyfin.Plugin.Necto.Configuration.Accounts.ProviderEnrollment { UserId = viewer.Id });

        await new UserDeletedConsumer(_config, NullLogger<UserDeletedConsumer>.Instance).OnEvent(new UserDeletedEventArgs(viewer));

        Assert.Empty(family.MemberIds);
        Assert.Empty(kids.MemberIds);
        Assert.Empty(_config.Configuration.ProviderEnrollments);
        Assert.Equal(1, _config.Saves);
    }
}
