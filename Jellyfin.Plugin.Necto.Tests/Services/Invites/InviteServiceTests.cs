using System;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.Accounts;
using Jellyfin.Plugin.Necto.Configuration.Groups;
using Jellyfin.Plugin.Necto.Configuration.Invites;
using Jellyfin.Plugin.Necto.Services.Invites;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Siphonophore.Stolon;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Invites;

/// <summary>
/// Invites are made, changed, turned on and off, and expire as the Invites tool and a member's page ask, and a
/// member's invites stay within what their groups allow.
/// </summary>
public sealed class InviteServiceTests : IDisposable
{
    private readonly InviteHarness _h = new();

    public void Dispose() => _h.Dispose();

    private Invite Stored(Guid id) => _h.Config.Configuration.Invites.Single(i => i.Id == id);

    /// <summary>
    /// A token is 48 lowercase hex characters, and two are never the same.
    /// True: a link cannot be guessed.
    /// False: links could collide or be short enough to guess.
    /// </summary>
    [Fact]
    public void NewToken_Is48LowercaseHexAndUnique()
    {
        var a = InviteService.NewToken();

        Assert.Matches("^[0-9a-f]{48}$", a);
        Assert.NotEqual(a, InviteService.NewToken());
    }

    /// <summary>
    /// A PIN is empty or six digits.
    /// True: the PIN reads like a Quick Connect code.
    /// False: a weak or odd PIN could be saved.
    /// </summary>
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("123456", true)]
    [InlineData(" 123456 ", true)]
    [InlineData("12345", false)]
    [InlineData("1234567", false)]
    [InlineData("12a456", false)]
    public void IsValidPin_RequiresSixDigitsOrNone(string? pin, bool expected) => Assert.Equal(expected, InviteService.IsValidPin(pin));

    /// <summary>
    /// A link is an absolute http or https address.
    /// True: a button on the signup page can only open a web page.
    /// False: a javascript: link could run on the signup page.
    /// </summary>
    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("http://example.com/guide", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("/relative", false)]
    [InlineData(null, false)]
    public void IsValidResourceUrl_RequiresAbsoluteHttp(string? url, bool expected) => Assert.Equal(expected, InviteService.IsValidResourceUrl(url));

    /// <summary>
    /// Expiry counts whole days, so an invite stops on its day.
    /// True: an invite set to expire today no longer works today.
    /// False: an invite would work for part of its last day.
    /// </summary>
    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void IsExpired_IsDayBased(int offset, bool expected)
    {
        var now = new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(expected, InviteService.IsExpired(new Invite { ExpiresAt = now.Date.AddDays(offset) }, now));
        Assert.False(InviteService.IsExpired(new Invite(), now));
    }

    /// <summary>
    /// A new invite is on, hashes its PIN, keeps its message and links, and writes an activity entry.
    /// True: the PIN is never stored as typed, and the Invites tool lists the new invite.
    /// False: a PIN could be read from the configuration.
    /// </summary>
    [Fact]
    public void Create_StoresTheInvite_WithAHashedPin()
    {
        var (invite, error) = _h.Service.Create(new InviteRequest { Label = "Family", Message = "Welcome", Pin = "123456", Resources = { new InviteResource { Title = "Guide", Url = "https://example.com" } } }, Guid.Empty, null);

        Assert.Null(error);
        Assert.True(invite!.HasPin);
        var stored = Stored(invite.Id);
        Assert.True(stored.Enabled);
        Assert.NotEqual("123456", stored.PinHash);
        Assert.Equal("Welcome", stored.Message);
        Assert.Single(stored.Resources);
        Assert.Contains("Invite created", _h.Activity.Names);
    }

    /// <summary>
    /// A bad PIN, a bad link, a missing group, or a group that disallows password changes is refused.
    /// True: the Invites tool shows why, and nothing is saved.
    /// False: an invite could be made that never works.
    /// </summary>
    [Theory]
    [InlineData("pin")]
    [InlineData("link")]
    [InlineData("group")]
    [InlineData("blocked")]
    public void Create_BadRequest_IsRefused(string what)
    {
        var blocked = TestGroups.Make("Managed", p => { p.Accounts.Password.Enabled = true; p.Accounts.Password.ChangeMode = PasswordChangeMode.Disallowed; });
        _h.Config.Configuration.Groups.Add(blocked);
        var request = what switch
        {
            "pin" => new InviteRequest { Pin = "12" },
            "link" => new InviteRequest { Resources = { new InviteResource { Title = "x", Url = "ftp://example.com" } } },
            "group" => new InviteRequest { GroupId = Guid.NewGuid() },
            _ => new InviteRequest { GroupId = blocked.Id }
        };

        var (invite, error) = _h.Service.Create(request, Guid.Empty, null);

        Assert.Null(invite);
        Assert.NotNull(error);
        Assert.Empty(_h.Config.Configuration.Invites);
    }

    /// <summary>
    /// A change keeps the PIN while none is sent, and an empty PIN removes it.
    /// True: editing an invite's name never drops its PIN by accident.
    /// False: every edit would clear or require the PIN again.
    /// </summary>
    [Fact]
    public void Update_KeepsThePinUnlessOneIsSent()
    {
        var (invite, _) = _h.Service.Create(new InviteRequest { Pin = "123456" }, Guid.Empty, null);

        _h.Service.Update(invite!.Id, new InviteRequest { Label = "Renamed" }, null);
        Assert.True(Stored(invite.Id).PinHash.Length > 0);
        Assert.Equal("Renamed", Stored(invite.Id).Label);

        _h.Service.Update(invite.Id, new InviteRequest { Label = "Renamed", Pin = string.Empty }, null);
        Assert.Empty(Stored(invite.Id).PinHash);
    }

    /// <summary>
    /// A duplicate copies the settings and the PIN with a new link.
    /// True: an administrator can hand out a fresh link with the same setup.
    /// False: the copy would share the original's link.
    /// </summary>
    [Fact]
    public void Duplicate_CopiesWithANewLink()
    {
        var (invite, _) = _h.Service.Create(new InviteRequest { Label = "Family", Pin = "123456" }, Guid.Empty, null);

        var copy = _h.Service.Duplicate(invite!.Id)!;

        Assert.NotEqual(invite.Token, copy.Token);
        Assert.Equal("Family", copy.Label);
        Assert.True(copy.HasPin);
        Assert.Null(_h.Service.Duplicate(Guid.NewGuid()));
    }

    /// <summary>
    /// Turning an invite on clears its wrong PINs, and is refused for an expired invite or a blocked group.
    /// True: a locked invite can be unlocked, but a dead one cannot be turned back on.
    /// False: an expired invite could be turned on, or an unlocked one would lock again at once.
    /// </summary>
    [Fact]
    public void SetEnabled_ClearsPinFailures_AndRefusesDeadInvites()
    {
        var (invite, _) = _h.Service.Create(new InviteRequest(), Guid.Empty, null);
        _h.Status.Update(d => { d.Invites[invite!.Id] = new InviteStatus { FailedPinAttempts = 4 }; return true; });
        _h.Service.SetEnabled(invite!.Id, false);

        Assert.Equal(InviteToggleResult.Updated, _h.Service.SetEnabled(invite.Id, true));
        Assert.Equal(0, _h.Status.Find(invite.Id)!.FailedPinAttempts);

        Stored(invite.Id).ExpiresAt = _h.Time.GetUtcNow().UtcDateTime.Date;
        Stored(invite.Id).Enabled = false;
        Assert.Equal(InviteToggleResult.Expired, _h.Service.SetEnabled(invite.Id, true));
        Assert.Equal(InviteToggleResult.Updated, _h.Service.SetEnabled(invite.Id, false));
        Assert.Equal(InviteToggleResult.NotFound, _h.Service.SetEnabled(Guid.NewGuid(), true));
    }

    /// <summary>
    /// Moving the day ahead turns an invite back on, and a day in the past does not.
    /// True: an expired invite comes back by moving its date.
    /// False: an administrator would have to make a new link.
    /// </summary>
    [Fact]
    public void SetExpiry_FutureDateRevives_PastDateDoesNot()
    {
        var (invite, _) = _h.Service.Create(new InviteRequest(), Guid.Empty, null);
        Stored(invite!.Id).Enabled = false;
        var today = _h.Time.GetUtcNow().UtcDateTime.Date;

        _h.Service.SetExpiry(invite.Id, today.AddDays(-1));
        Assert.False(Stored(invite.Id).Enabled);

        _h.Service.SetExpiry(invite.Id, today.AddDays(3));
        Assert.True(Stored(invite.Id).Enabled);
        Assert.False(_h.Service.SetExpiry(Guid.NewGuid(), null));
    }

    /// <summary>
    /// The expiry run turns off each invite whose day came, in one activity entry and one event.
    /// True: the log reads "Expired invites" once, and channels that pick the event hear about it.
    /// False: an expired invite would stay on, or the log would get one entry per invite.
    /// </summary>
    [Fact]
    public void ExpireInvites_TurnsOffDueInvites()
    {
        var today = _h.Time.GetUtcNow().UtcDateTime.Date;
        _h.Service.Create(new InviteRequest { Label = "Old", ExpiresAt = today.AddDays(-1) }, Guid.Empty, null);
        _h.Service.Create(new InviteRequest { Label = "Older", ExpiresAt = today }, Guid.Empty, null);
        _h.Service.Create(new InviteRequest { Label = "Fresh", ExpiresAt = today.AddDays(2) }, Guid.Empty, null);

        Assert.Equal(2, _h.Service.ExpireInvites());

        Assert.Single(_h.Config.Configuration.Invites, i => i.Enabled);
        Assert.Contains("Expired invites", _h.Activity.Names);
        Assert.Contains(_h.Notifier.Events, e => e.Key == InvitesEvents.Expired);
    }

    /// <summary>
    /// An invite whose group switches to disallow password changes is turned off on the next save.
    /// True: a group managed by administrators takes no signups.
    /// False: a signup could make an account no one can set a password for.
    /// </summary>
    [Fact]
    public void Normalize_GroupTurnsBlocking_TurnsItsInvitesOff()
    {
        var group = TestGroups.Make("Family");
        _h.Config.Configuration.Groups.Add(group);
        _h.Config.Configuration.GroupSettings.NewUserGroupId = group.Id;
        var (byGroup, _) = _h.Service.Create(new InviteRequest { GroupId = group.Id }, Guid.Empty, null);
        var (byDefault, _) = _h.Service.Create(new InviteRequest(), Guid.Empty, null);

        group.Permissions.Accounts.Password.Enabled = true;
        group.Permissions.Accounts.Password.ChangeMode = PasswordChangeMode.Disallowed;

        Assert.Equal(2, InviteNormalizer.Normalize(_h.Config.Configuration));
        Assert.False(Stored(byGroup!.Id).Enabled);
        Assert.False(Stored(byDefault!.Id).Enabled);
        Assert.Equal(InviteToggleResult.GroupBlocksInvites, _h.Service.SetEnabled(byGroup.Id, true));
    }

    /// <summary>
    /// The summaries carry each invite's state, use count, and group, without its PIN hash.
    /// True: the Invites tool shows each invite as Active, Locked, Expired, Group expired, or Used up.
    /// False: a used up invite would read as Active.
    /// </summary>
    [Fact]
    public void Summaries_CarryStateAndCounts()
    {
        var (invite, _) = _h.Service.Create(new InviteRequest { MaxUses = 2 }, Guid.Empty, null);
        _h.Status.Update(d => { d.Invites[invite!.Id] = new InviteStatus { UsedCount = 2 }; return true; });

        var row = Assert.Single(_h.Service.Summaries());

        Assert.Equal(InviteState.UsedUp, row.State);
        Assert.Equal(2, row.UsedCount);
        Assert.Equal("Everyone", row.GroupName);
    }

    /// <summary>
    /// A member may make invites only as their groups allow, up to their open invite count, into the groups they may
    /// target.
    /// True: invite permissions on groups decide who invites, how many, and where.
    /// False: any member could flood the server with invites into any group.
    /// </summary>
    [Fact]
    public void CreateForMember_FollowsTheGroups()
    {
        var target = TestGroups.Make("Guests");
        var other = TestGroups.Make("Staff");
        var member = _h.Users.Add("member");
        var inviters = TestGroups.Make("Inviters", p => { p.Invites.MayCreate = true; p.Invites.MaxInvites = 1; p.Invites.TargetGroupIds.Add(target.Id); }, member.Id);
        _h.Config.Configuration.Groups.AddRange(new[] { target, other, inviters });
        var outsider = _h.Users.Add("outsider");

        Assert.NotNull(_h.Service.CreateForMember(outsider.Id, new InviteRequest { GroupId = target.Id }, null).Error);
        Assert.NotNull(_h.Service.CreateForMember(member.Id, new InviteRequest { GroupId = other.Id }, null).Error);
        var (mine, error) = _h.Service.CreateForMember(member.Id, new InviteRequest { GroupId = target.Id }, null);
        Assert.Null(error);
        Assert.Equal(member.Id, mine!.CreatedBy);
        Assert.NotNull(_h.Service.CreateForMember(member.Id, new InviteRequest { GroupId = target.Id }, null).Error);
        Assert.Single(_h.Service.SummariesOf(member.Id));
        Assert.Contains(_h.Notifier.Events, e => e.Key == InvitesEvents.Created && e.UserId == member.Id);
    }

    /// <summary>
    /// A member allowed only Everyone invites into Everyone, even with a group for new users set.
    /// True: the member's invite signs users into no group, as they picked.
    /// False: the invite is refused, or lands users in the group for new users.
    /// </summary>
    [Fact]
    public void CreateForMember_IntoEveryone_KeepsEveryone()
    {
        var family = TestGroups.Make("Family");
        var member = _h.Users.Add("member");
        var inviters = TestGroups.Make("Inviters", p => { p.Invites.MayCreate = true; p.Invites.TargetGroupIds.Add(Group.EveryoneId); }, member.Id);
        _h.Config.Configuration.Groups.AddRange(new[] { family, inviters });
        _h.Config.Configuration.GroupSettings.NewUserGroupId = family.Id;

        var (mine, error) = _h.Service.CreateForMember(member.Id, new InviteRequest { GroupId = Group.EveryoneId }, null);

        Assert.Null(error);
        Assert.Equal(Group.EveryoneId, mine!.GroupId);
        Assert.NotNull(_h.Service.CreateForMember(member.Id, new InviteRequest { GroupId = family.Id }, null).Error);
    }

    /// <summary>
    /// A member deletes only their own invites.
    /// True: members manage their own links and nobody else's.
    /// False: a member could delete an administrator's invite.
    /// </summary>
    [Fact]
    public void Delete_ForAMember_TouchesOnlyTheirs()
    {
        var (theirs, _) = _h.Service.Create(new InviteRequest(), Guid.Empty, null);
        var member = Guid.NewGuid();

        Assert.False(_h.Service.Delete(theirs!.Id, member));
        Assert.True(_h.Service.Delete(theirs.Id));
        Assert.Empty(_h.Config.Configuration.Invites);
    }

    /// <summary>
    /// The Invites tool's table pages the invites, keeps those in the state its filter names, and searches each name,
    /// group, and maker.
    /// True: the table's state filter and search find the right invites, a page at a time.
    /// False: a filter or search could show invites in another state, or miss one by its group.
    /// </summary>
    [Fact]
    public void PageOf_FiltersByStateSearchesAndPages()
    {
        var all = new[]
        {
            new InviteSummary { Label = "Family", GroupName = "Family", State = InviteState.Active },
            new InviteSummary { Label = "Cousins", GroupName = "Family", State = InviteState.UsedUp },
            new InviteSummary { Label = "Book club", GroupName = "Everyone", State = InviteState.Active, CreatedByName = "viewer" },
            new InviteSummary { Label = "Old", GroupName = "Friends", State = InviteState.Expired }
        };

        Assert.Equal(4, InviteService.PageOf(all, null).TotalCount);
        Assert.Equal(4, InviteService.PageOf(all, new() { Filter = "All" }).TotalCount);
        Assert.Equal(new[] { "Family", "Book club" }, InviteService.PageOf(all, new() { Filter = "Active" }).Items.Select(i => i.Label));
        Assert.Equal(new[] { "Cousins" }, InviteService.PageOf(all, new() { Filter = "usedup" }).Items.Select(i => i.Label));
        Assert.Empty(InviteService.PageOf(all, new() { Filter = "1" }).Items);
        Assert.Empty(InviteService.PageOf(all, new() { Filter = "Nope" }).Items);
        Assert.Equal(new[] { "Family", "Cousins" }, InviteService.PageOf(all, new() { Search = "fam" }).Items.Select(i => i.Label));
        Assert.Equal(new[] { "Book club" }, InviteService.PageOf(all, new() { Search = "VIEWER" }).Items.Select(i => i.Label));
        Assert.Equal(new[] { "Cousins" }, InviteService.PageOf(all, new() { Filter = "UsedUp", Search = "family" }).Items.Select(i => i.Label));

        var page = InviteService.PageOf(all, new() { Skip = 2, Take = 1 });
        Assert.Equal("Book club", Assert.Single(page.Items).Label);
        Assert.Equal(4, page.TotalCount);
        Assert.True(page.HasMore);
    }
}
