using System;
using Jellyfin.Plugin.Necto.Configuration.CustomPages;
using Jellyfin.Plugin.Necto.Configuration.Groups;
using Jellyfin.Plugin.Necto.Services.CustomPages;
using Jellyfin.Plugin.Necto.Services.Groups;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.CustomPages;

/// <summary>
/// Who may open a page follows its tier and, for a Signed in page, the groups that give it.
/// </summary>
public class PageAccessTests
{
    private static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static CustomPage Page(PageVisibility visibility) => new() { Id = "p1", Slug = "p", Visibility = visibility };

    private static EffectivePermissions Member(Action<CustomPagePermissions> give)
    {
        var group = TestGroups.Make("Family", p => give(p.CustomPages), Alice);
        return PermissionService.Resolve(TestGroups.Config(group), Alice, isAdministrator: false);
    }

    /// <summary>
    /// An Anyone page opens for every caller, signed in or not.
    /// True: a public page stays public.
    /// False: visitors who are not signed in cannot open a public page.
    /// </summary>
    [Fact]
    public void MayOpen_AnyonePage_OpensForEveryone()
    {
        Assert.True(PageAccess.MayOpen(Page(PageVisibility.Anonymous), null));
        Assert.True(PageAccess.MayOpen(Page(PageVisibility.Anonymous), Member(_ => { })));
    }

    /// <summary>
    /// A Signed in page opens only for a member whose group names it or gives every page.
    /// True: groups decide who reads a page.
    /// False: a page opens for groups that never gave it.
    /// </summary>
    [Theory]
    [InlineData(false, "p1", true)]
    [InlineData(false, "p2", false)]
    [InlineData(true, "p2", true)]
    public void MayOpen_SignedInPage_FollowsTheGroups(bool allPages, string listed, bool expected)
    {
        var member = Member(p =>
        {
            p.AllPages = allPages;
            p.PageIds.Add(listed);
        });

        Assert.Equal(expected, PageAccess.MayOpen(Page(PageVisibility.User), member));
    }

    /// <summary>
    /// A caller with no user opens no Signed in or Administrators page.
    /// True: an API key or a visitor who is not signed in never reads a gated page.
    /// False: a gated page opens without a user.
    /// </summary>
    [Theory]
    [InlineData(PageVisibility.User)]
    [InlineData(PageVisibility.Admin)]
    public void MayOpen_GatedPage_RefusesACallerWithNoUser(PageVisibility visibility)
        => Assert.False(PageAccess.MayOpen(Page(visibility), null));

    /// <summary>
    /// An Administrators page opens for administrators only, whatever a group gives.
    /// True: no group can open an Administrators page to members.
    /// False: a group with every page reads the administrators' pages.
    /// </summary>
    [Fact]
    public void MayOpen_AdminPage_OpensForAdministratorsOnly()
    {
        Assert.False(PageAccess.MayOpen(Page(PageVisibility.Admin), Member(p => p.AllPages = true)));
        Assert.True(PageAccess.MayOpen(Page(PageVisibility.Admin), PermissionMerger.Administrator(Alice)));
    }

    /// <summary>
    /// An administrator opens every Signed in page.
    /// True: administrators never need a group for their own pages.
    /// False: an administrator is locked out of a page no group gives.
    /// </summary>
    [Fact]
    public void MayOpen_SignedInPage_OpensForAnAdministrator()
        => Assert.True(PageAccess.MayOpen(Page(PageVisibility.User), PermissionMerger.Administrator(Alice)));

    /// <summary>
    /// A user the server does not have opens nothing gated.
    /// True: a deleted user's token reads no page.
    /// False: a removed account keeps reading pages.
    /// </summary>
    [Fact]
    public void MayOpen_UnknownUser_IsRefused()
        => Assert.False(PageAccess.MayOpen(Page(PageVisibility.User), PermissionMerger.Nobody(Alice)));

    /// <summary>
    /// A tier no enum value names opens for nobody.
    /// True: a hand edited tier cannot unlock a page.
    /// False: an out of range tier opens for everyone.
    /// </summary>
    [Fact]
    public void MayOpen_UnknownTier_IsRefused()
        => Assert.False(PageAccess.MayOpen(Page((PageVisibility)9), PermissionMerger.Administrator(Alice)));
}
