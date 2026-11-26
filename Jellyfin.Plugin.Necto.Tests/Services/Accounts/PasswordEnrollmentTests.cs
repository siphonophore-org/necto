using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Necto.Configuration.Accounts;
using Jellyfin.Plugin.Necto.Services.Accounts;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Accounts;

/// <summary>
/// Users move onto the password rule provider while rules hold for them and back to their own provider after, and
/// administrators and users signed in elsewhere never move onto it.
/// </summary>
public class PasswordEnrollmentTests
{
    private readonly FakeUsers _users = new();
    private readonly FakeConfigurationStore _config = new();

    private PasswordEnrollment Enrollment() => new(_config, _users.Manager, new AccountActivity(new FakeActivity().Logger, FakeServerConfig.Create()), NullLogger<PasswordEnrollment>.Instance);

    private void Enforce(params System.Guid[] members)
        => _config.Configuration.Groups.Add(TestGroups.Make("Ruled", p => p.Accounts.Password.Enabled = true, members));

    /// <summary>
    /// A member of an enforcing group on Jellyfin's own provider moves onto Necto's, through the policy.
    /// True: the rules check the member's next password.
    /// False: the rules would never run for the member.
    /// </summary>
    [Fact]
    public async Task Reconcile_RuledMember_IsEnrolled()
    {
        var viewer = _users.Add("viewer");
        Enforce(viewer.Id);

        Assert.Equal(1, await Enrollment().ReconcileAsync());

        Assert.Equal(PasswordEnrollment.ProviderId, _users.Policies[viewer.Id].AuthenticationProviderId);
        var record = Assert.Single(_config.Configuration.ProviderEnrollments);
        Assert.Equal(FakeUsers.DefaultProvider, record.OriginalProviderId);
    }

    /// <summary>
    /// Everyone's rules hold for users in no group.
    /// True: rules on Everyone reach every ungrouped user.
    /// False: ungrouped users would escape Everyone's rules.
    /// </summary>
    [Fact]
    public async Task Reconcile_EveryoneRules_EnrollUngroupedUsers()
    {
        var viewer = _users.Add("viewer");
        _config.Configuration.Everyone.Permissions.Accounts.Password.Enabled = true;

        await Enrollment().ReconcileAsync();

        Assert.Equal(PasswordEnrollment.ProviderId, _users.Users[viewer.Id].AuthenticationProviderId);
    }

    /// <summary>
    /// A user signed in elsewhere, such as LDAP, stays on their provider.
    /// True: an external account keeps signing in where it does.
    /// False: the account would become a blank password sign in.
    /// </summary>
    [Fact]
    public async Task Reconcile_ExternalProvider_IsLeftAlone()
    {
        var viewer = _users.Add("viewer", provider: "Ldap");
        Enforce(viewer.Id);

        Assert.Equal(0, await Enrollment().ReconcileAsync());

        Assert.Equal("Ldap", _users.Users[viewer.Id].AuthenticationProviderId);
    }

    /// <summary>
    /// A member promoted to administrator moves back to the provider they had.
    /// True: an administrator is never held to rules and can sign in once Necto is gone.
    /// False: a promoted member would stay on Necto's provider.
    /// </summary>
    [Fact]
    public async Task Reconcile_PromotedMember_MovesBack()
    {
        var viewer = _users.Add("viewer");
        Enforce(viewer.Id);
        await Enrollment().ReconcileAsync();
        _users.Users[viewer.Id].SetPermission(PermissionKind.IsAdministrator, true);

        await Enrollment().ReconcileAsync();

        Assert.Equal(FakeUsers.DefaultProvider, _users.Policies[viewer.Id].AuthenticationProviderId);
        Assert.Empty(_config.Configuration.ProviderEnrollments);
    }

    /// <summary>
    /// A user whose groups stop enforcing rules moves back, to the provider recorded for them.
    /// True: turning rules off frees the members.
    /// False: members would stay on Necto's provider after the rules are gone.
    /// </summary>
    [Fact]
    public async Task Reconcile_RulesTurnedOff_MovesBackToTheRecordedProvider()
    {
        var viewer = _users.Add("viewer", provider: PasswordEnrollment.ProviderId);
        _config.Configuration.ProviderEnrollments.Add(new ProviderEnrollment { UserId = viewer.Id, OriginalProviderId = "Custom" });

        Assert.Equal(1, await Enrollment().ReconcileAsync());

        Assert.Equal("Custom", _users.Users[viewer.Id].AuthenticationProviderId);
    }

    /// <summary>
    /// An administrator who was never enrolled is left alone.
    /// True: the pass never touches an administrator's provider.
    /// False: an administrator's policy would be written for nothing.
    /// </summary>
    [Fact]
    public async Task Reconcile_Administrator_IsLeftAlone()
    {
        var admin = _users.Add("admin", admin: true);
        _config.Configuration.Everyone.Permissions.Accounts.Password.Enabled = true;

        Assert.Equal(0, await Enrollment().ReconcileAsync());

        Assert.Equal(FakeUsers.DefaultProvider, _users.Users[admin.Id].AuthenticationProviderId);
    }
}
