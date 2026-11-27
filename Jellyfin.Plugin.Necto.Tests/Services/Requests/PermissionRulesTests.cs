using System;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Services.Groups;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Requests;

/// <summary>
/// A user's Requests and Issues grant comes from their groups and the windows in Settings: their own groups or
/// Everyone, everything for administrators, nothing for an unknown user.
/// </summary>
[Collection(ServicesCollection.Name)]
public class PermissionRulesTests
{
    private static readonly Guid Member = Guid.NewGuid();

    /// <summary>
    /// A member's grant comes from their group and not Everyone's, and anyone else's from Everyone, with the windows from Settings.
    /// True: a Family member gets Family's limits and profiles, and someone in no group gets Everyone's.
    /// False: Everyone's limits would apply to a group's members, or a group's to everyone.
    /// </summary>
    [Fact]
    public void For_Groups_GrantTheirOwnOrEveryones()
    {
        var config = TestGroups.Config(TestGroups.Make("Family", p =>
        {
            p.Requests.MayRequest = true;
            p.Requests.MaxOpenRequests = 0;
            p.Requests.MovieQuota = 10;
            p.Requests.MovieProfiles = new() { "fourk" };
        }, Member));
        config.Everyone.Permissions.Requests.MayRequest = true;
        config.Everyone.Permissions.Requests.MaxOpenRequests = 5;
        config.Everyone.Permissions.Requests.MovieQuota = 3;
        config.Everyone.Permissions.Requests.Series = false;
        config.Requests.MovieQuotaDays = 14;

        var mine = PermissionRules.For(config, Member, false);
        var anyone = PermissionRules.For(config, Guid.NewGuid(), false);

        Assert.Equal(0, mine.MaxOpenRequests);
        Assert.Equal(10, mine.MovieQuotaLimit);
        Assert.True(mine.Shows);
        Assert.Equal(new[] { "fourk" }, mine.MovieProfiles);
        Assert.Equal(14, mine.MovieQuotaDays);
        Assert.Equal(5, anyone.MaxOpenRequests);
        Assert.Equal(3, anyone.MovieQuotaLimit);
        Assert.False(anyone.Shows);
        Assert.True(anyone.CanRequest);
        Assert.False(anyone.ManageRequests);
    }

    /// <summary>
    /// Administrators may always request every type, see everything, are approved at once, never limited, and manage both tools.
    /// True: a server administrator is never held back by a group.
    /// False: an administrator in a limited group would be limited.
    /// </summary>
    [Fact]
    public void For_Administrator_IsGrantedEverything()
    {
        var config = TestGroups.Config(TestGroups.Make("Kids", p => { p.Requests.MayRequest = true; p.Requests.MaxOpenRequests = 1; }, Member));

        var admin = PermissionRules.For(config, Member, true);

        Assert.True(admin.CanRequest);
        Assert.True(admin.Movies);
        Assert.True(admin.Shows);
        Assert.True(admin.Unreleased);
        Assert.True(admin.AutoApproveMovies);
        Assert.True(admin.AutoApproveShows);
        Assert.True(admin.SeeAllRequests);
        Assert.True(admin.SeeRequesters);
        Assert.Equal(0, admin.MaxOpenRequests);
        Assert.Equal(0, admin.MovieQuotaLimit);
        Assert.Equal(0, admin.ShowQuotaLimit);
        Assert.Equal(0, admin.MaxSeasons);
        Assert.Equal(0, admin.IssueQuotaLimit);
        Assert.True(admin.ReportIssues);
        Assert.True(admin.ManageRequests);
        Assert.True(admin.ManageIssues);
    }

    /// <summary>
    /// A user the server does not have is granted nothing.
    /// True: an id that matches no user can neither request nor report.
    /// False: a deleted user's id would still act.
    /// </summary>
    [Fact]
    public void From_UnknownUser_GrantsNothing()
    {
        var grant = PermissionRules.From(PermissionMerger.Nobody(Guid.NewGuid()), new RequestSettings(), 1);

        Assert.False(grant.CanRequest);
        Assert.False(grant.Movies);
        Assert.False(grant.Shows);
        Assert.False(grant.ReportIssues);
        Assert.False(grant.ManageRequests);
        Assert.False(grant.ManageIssues);
    }

    /// <summary>
    /// Seeing others' requests, and who made them, needs a group that allows requesting.
    /// True: a group that only sees cannot see without requesting.
    /// False: a user barred from requesting could still read everyone's requests.
    /// </summary>
    [Fact]
    public void For_SeeingOthers_NeedsRequesting()
    {
        var config = new PluginConfiguration();
        config.Everyone.Permissions.Requests.MaySeeOthers = true;
        config.Everyone.Permissions.Requests.MaySeeRequesters = true;

        var blocked = PermissionRules.For(config, Guid.NewGuid(), false);
        config.Everyone.Permissions.Requests.MayRequest = true;
        var allowed = PermissionRules.For(config, Guid.NewGuid(), false);

        Assert.False(blocked.SeeAllRequests);
        Assert.False(blocked.SeeRequesters);
        Assert.True(allowed.SeeAllRequests);
        Assert.True(allowed.SeeRequesters);
    }

    /// <summary>
    /// The quota and issue windows come from Settings, never below one day.
    /// True: a quota counts over the window Settings sets, for movies, shows, and issues apart.
    /// False: a window of 0 would let a quota reset every moment.
    /// </summary>
    [Fact]
    public void For_Windows_ComeFromSettingsAtLeastADay()
    {
        var config = new PluginConfiguration();
        config.Everyone.Permissions.Requests.MayRequest = true;
        config.Everyone.Permissions.Issues.MayReport = true;
        config.Everyone.Permissions.Issues.Limit = 4;
        config.Requests.MovieQuotaDays = 30;
        config.Requests.SeriesQuotaDays = 0;
        config.Issues.LimitDays = 3;

        var grant = PermissionRules.For(config, Guid.NewGuid(), false);

        Assert.Equal(30, grant.MovieQuotaDays);
        Assert.Equal(1, grant.ShowQuotaDays);
        Assert.Equal(3, grant.IssueQuotaDays);
        Assert.Equal(4, grant.IssueQuotaLimit);
        Assert.True(grant.ReportIssues);
    }

    /// <summary>
    /// Managing comes from a group's manage permission, apart for each tool.
    /// True: a moderator group manages requests without managing issues.
    /// False: one manage permission would grant the other.
    /// </summary>
    [Fact]
    public void For_ManagingGroup_ManagesItsToolOnly()
    {
        var config = TestGroups.Config(TestGroups.Make("Moderators", p => p.Requests.MayManage = true, Member));

        var grant = PermissionRules.For(config, Member, false);

        Assert.True(grant.ManageRequests);
        Assert.False(grant.ManageIssues);
        Assert.False(grant.CanRequest);
    }

    /// <summary>
    /// The grant from the loaded plugin uses its groups, whatever settings the caller holds.
    /// True: a service holding only the Requests settings still gets the user's groups.
    /// False: the grant would ignore the groups and use stale windows.
    /// </summary>
    [Fact]
    public void For_LoadedPlugin_UsesItsGroups()
    {
        using var harness = new RequestHarness();
        var user = harness.AddUser("limited", permissions: p => p.Requests.MaxOpenRequests = 3);

        var grant = PermissionRules.For(new RequestSettings { MovieQuotaDays = 99 }, user.Id, false);

        Assert.Equal(3, grant.MaxOpenRequests);
        Assert.Equal(7, grant.MovieQuotaDays);
    }

    /// <summary>
    /// The longest quota window counts only where a requesting group has a quota, and is 0 when none has.
    /// True: cleanup keeps requests as long as a quota still counts them, and no longer.
    /// False: cleanup would remove requests a quota still counts, or keep them for no quota.
    /// </summary>
    [Fact]
    public void LongestQuotaDays_Quotas_TakeTheirWindow()
    {
        var config = new PluginConfiguration();
        config.Requests.MovieQuotaDays = 14;
        config.Requests.SeriesQuotaDays = 30;
        config.Everyone.Permissions.Requests.MayRequest = true;

        Assert.Equal(0, PermissionRules.LongestQuotaDays(config));

        config.Everyone.Permissions.Requests.MovieQuota = 2;
        Assert.Equal(14, PermissionRules.LongestQuotaDays(config));

        config.Groups.Add(TestGroups.Make("Shows", p => { p.Requests.MayRequest = true; p.Requests.SeriesQuota = 1; }));
        Assert.Equal(30, PermissionRules.LongestQuotaDays(config));

        config.Groups.Add(TestGroups.Make("Off", p => p.Requests.SeriesQuota = 5));
        config.Groups.RemoveAt(0);
        Assert.Equal(14, PermissionRules.LongestQuotaDays(config));
    }
}
