using System;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Configuration.Accounts;
using Jellyfin.Plugin.Necto.Configuration.Invites;
using Jellyfin.Plugin.Necto.Services.Accounts;
using Jellyfin.Plugin.Necto.Services.Invites;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Invites;

/// <summary>
/// Using an invite makes exactly one account in the invite's group, held disabled until its password is set, and
/// refuses every use the invite, its group, or its counters do not allow.
/// </summary>
public sealed class InviteRedemptionTests : IDisposable
{
    private readonly InviteHarness _h = new();

    public void Dispose() => _h.Dispose();

    private Invite Make(InviteRequest? request = null)
    {
        var (summary, error) = _h.Service.Create(request ?? new InviteRequest(), Guid.Empty, null);
        Assert.Null(error);
        return _h.Config.Configuration.Invites.Single(i => i.Id == summary!.Id);
    }

    /// <summary>
    /// While Invites is off, an invite link says the invite is unavailable and makes no account.
    /// True: turning Invites off stops every signup.
    /// False: an old link would still make accounts while Invites is off.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Redeem_InvitesOff_SaysTheInviteIsUnavailable()
    {
        var invite = Make(new InviteRequest { Label = "Family" });
        _h.Config.Configuration.InviteSettings.Enabled = false;

        var result = await _h.RedeemAsync(invite.Token);

        Assert.False(result.Success);
        Assert.Equal("This invite is no longer available.", result.Message);
        Assert.Empty(_h.Users.Users.Values);
        Assert.False(_h.Service.IsRedeemableNow(invite));
        _h.Config.Configuration.InviteSettings.Enabled = true;
        Assert.True(_h.Service.IsRedeemableNow(invite));
    }

    /// <summary>
    /// A signup makes the account, sets its password, counts a use, and writes the activity entry and the event.
    /// True: the invitee can sign in, and administrators see who joined.
    /// False: an invite would make no account, or make one nobody hears about.
    /// </summary>
    [Fact]
    public async Task Redeem_HappyPath_MakesTheAccountAndCountsAUse()
    {
        var invite = Make(new InviteRequest { Label = "Family", Resources = { new InviteResource { Title = "Guide", Url = "https://example.com" } } });

        var result = await _h.RedeemAsync(invite.Token);

        Assert.True(result.Success, result.Message);
        var user = Assert.Single(_h.Users.Users.Values);
        Assert.Equal("viewer", user.Username);
        Assert.Equal("Secret1!", _h.Users.Passwords[user.Id]);
        Assert.False(_h.Users.IsDisabled(user.Id));
        Assert.Equal(1, _h.Status.Find(invite.Id)!.UsedCount);
        Assert.Single(result.Resources);
        Assert.Contains("Account made with an invite", _h.Activity.Names);
        Assert.Contains(_h.Notifier.Events, e => e.Key == InvitesEvents.Used);
    }

    /// <summary>
    /// The account is held disabled while its password is set.
    /// True: nobody can sign in to a new account before it has its password.
    /// False: an account with no password would be open for a moment.
    /// </summary>
    [Fact]
    public async Task Redeem_AccountIsDisabledUntilThePasswordIsSet()
    {
        var invite = Make();

        await _h.RedeemAsync(invite.Token);

        Assert.True(_h.DisabledWhenCreated);
    }

    /// <summary>
    /// The account joins the invite's group only, never the group for new users.
    /// True: an invite to Guests gives Guests' permissions and nothing of the default group.
    /// False: the new account would carry the default group too.
    /// </summary>
    [Fact]
    public async Task Redeem_JoinsOnlyTheInvitesGroup()
    {
        var guests = TestGroups.Make("Guests");
        var family = TestGroups.Make("Family");
        _h.Config.Configuration.Groups.AddRange(new[] { guests, family });
        _h.Config.Configuration.GroupSettings.NewUserGroupId = family.Id;

        await _h.RedeemAsync(Make(new InviteRequest { GroupId = guests.Id }).Token);

        var id = _h.Users.Users.Values.Single().Id;
        Assert.Contains(id, guests.MemberIds);
        Assert.DoesNotContain(id, family.MemberIds);
    }

    /// <summary>
    /// An invite into Everyone keeps Everyone and signs users into no group, even with a group for new users set.
    /// True: an administrator or member who picked Everyone gets accounts in no group.
    /// False: the account would land in the group for new users.
    /// </summary>
    [Fact]
    public async Task Redeem_IntoEveryone_JoinsNoGroup()
    {
        var family = TestGroups.Make("Family");
        _h.Config.Configuration.Groups.Add(family);
        _h.Config.Configuration.GroupSettings.NewUserGroupId = family.Id;
        var invite = Make(new InviteRequest { GroupId = Jellyfin.Plugin.Necto.Configuration.Groups.Group.EveryoneId });

        await _h.RedeemAsync(invite.Token);

        Assert.Equal(Jellyfin.Plugin.Necto.Configuration.Groups.Group.EveryoneId, invite.GroupId);
        Assert.Empty(family.MemberIds);
        Assert.Equal("Everyone", Assert.Single(_h.Service.Summaries()).GroupName);
    }

    /// <summary>
    /// An invite with no group, or whose group was deleted, uses the group for new users.
    /// True: a default invite follows Settings at the moment it is used.
    /// False: the account would land in no group.
    /// </summary>
    [Fact]
    public async Task Redeem_NoOrDeletedGroup_UsesTheGroupForNewUsers()
    {
        var family = TestGroups.Make("Family");
        var gone = TestGroups.Make("Gone");
        _h.Config.Configuration.Groups.AddRange(new[] { family, gone });
        _h.Config.Configuration.GroupSettings.NewUserGroupId = family.Id;
        var invite = Make(new InviteRequest { GroupId = gone.Id });
        _h.Config.Configuration.Groups.Remove(gone);

        await _h.RedeemAsync(invite.Token);

        Assert.Contains(_h.Users.Users.Values.Single().Id, family.MemberIds);
    }

    /// <summary>
    /// A group that enforces password rules moves the account onto them, and a password that breaks them is refused.
    /// True: an invited account is held to its group's rules from the first password.
    /// False: an invitee could pick a password the group forbids.
    /// </summary>
    [Fact]
    public async Task Redeem_GroupPasswordRules_AreCheckedAndEnrolled()
    {
        var strict = TestGroups.Make("Strict", p => { p.Accounts.Password.Enabled = true; p.Accounts.Password.MinLength = 12; });
        _h.Config.Configuration.Groups.Add(strict);
        var invite = Make(new InviteRequest { GroupId = strict.Id });

        Assert.False((await _h.RedeemAsync(invite.Token, password: "short")).Success);
        Assert.Empty(_h.Users.Users);

        Assert.True((await _h.RedeemAsync(invite.Token, password: "a-long-enough-one")).Success);
        Assert.Equal(PasswordEnrollment.ProviderId, _h.Users.Users.Values.Single().AuthenticationProviderId);
    }

    /// <summary>
    /// A group whose template disables accounts keeps the account disabled.
    /// True: a group that holds new members for review keeps them off.
    /// False: the signup would turn the account on regardless.
    /// </summary>
    [Fact]
    public async Task Redeem_TemplateDisablesAccounts_KeepsItDisabled()
    {
        var review = TestGroups.Make("Review");
        review.Template.Enabled = true;
        review.Template.Access.ManageIsDisabled = true;
        review.Template.Access.IsDisabled = true;
        _h.Config.Configuration.Groups.Add(review);

        await _h.RedeemAsync(Make(new InviteRequest { GroupId = review.Id }).Token);

        Assert.True(_h.Users.IsDisabled(_h.Users.Users.Values.Single().Id));
    }

    /// <summary>
    /// An expired group, a group that disallows password changes, an off invite, an expired invite, a used up one, and
    /// a busy one are each refused.
    /// True: an invite makes accounts only while everything allows it.
    /// False: an invite could make an account that is already dead or unwanted.
    /// </summary>
    [Theory]
    [InlineData("groupExpired")]
    [InlineData("groupBlocks")]
    [InlineData("off")]
    [InlineData("expired")]
    [InlineData("usedUp")]
    [InlineData("rate")]
    [InlineData("noToken")]
    public async Task Redeem_DeadInvite_IsRefused(string why)
    {
        var group = TestGroups.Make("Family");
        _h.Config.Configuration.Groups.Add(group);
        var invite = Make(new InviteRequest { GroupId = group.Id, MaxUses = 1 });
        var today = _h.Time.GetUtcNow().UtcDateTime;
        switch (why)
        {
            case "groupExpired": group.Permissions.Accounts.Expiry.ExpiresOn = today.Date; break;
            case "groupBlocks": group.Permissions.Accounts.Password.Enabled = true; group.Permissions.Accounts.Password.ChangeMode = PasswordChangeMode.Disallowed; break;
            case "off": invite.Enabled = false; break;
            case "expired": invite.ExpiresAt = today.Date; break;
            case "usedUp": _h.Status.Update(d => { d.Invites[invite.Id] = new InviteStatus { UsedCount = 1 }; return true; }); break;
            case "rate":
                invite.MaxUses = 0;
                _h.Status.Update(d => { d.Invites[invite.Id] = new InviteStatus { RecentRedemptions = Enumerable.Repeat(today, 10).ToList() }; return true; });
                break;
        }

        var result = await _h.RedeemAsync(why == "noToken" ? "nope" : invite.Token);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Message);
        Assert.Empty(_h.Users.Users);
    }

    /// <summary>
    /// A wrong PIN counts, and the last allowed wrong PIN locks the invite with an entry and an event.
    /// True: guessing a PIN locks the link for everyone until an administrator turns it on.
    /// False: a PIN could be guessed without end.
    /// </summary>
    [Fact]
    public async Task Redeem_WrongPins_LockTheInvite()
    {
        _h.Config.Configuration.InviteSettings.MaxPinAttempts = 2;
        var invite = Make(new InviteRequest { Pin = "123456" });

        Assert.False((await _h.RedeemAsync(invite.Token, pin: "000000")).Success);
        Assert.True(invite.Enabled);
        Assert.Equal(1, _h.Status.Find(invite.Id)!.FailedPinAttempts);

        Assert.False((await _h.RedeemAsync(invite.Token, pin: "000000")).Success);
        Assert.False(invite.Enabled);
        Assert.Contains("Invite locked by PIN", _h.Activity.Names);
        Assert.Contains(_h.Notifier.Events, e => e.Key == InvitesEvents.Locked);

        Assert.False((await _h.RedeemAsync(invite.Token, pin: "123456")).Success);
    }

    /// <summary>
    /// The right PIN makes the account and clears the wrong PINs.
    /// True: an invitee who mistyped once can still sign up.
    /// False: an earlier typo would count toward a lock forever.
    /// </summary>
    [Fact]
    public async Task Redeem_RightPin_ClearsWrongPins()
    {
        var invite = Make(new InviteRequest { Pin = "123456" });
        await _h.RedeemAsync(invite.Token, pin: "000000");

        Assert.True((await _h.RedeemAsync(invite.Token, pin: "123456")).Success);
        Assert.Equal(0, _h.Status.Find(invite.Id)!.FailedPinAttempts);
    }

    /// <summary>
    /// A taken user name gets a reply that does not say the name is taken.
    /// True: the signup page cannot be used to find out who has an account.
    /// False: a stranger could list the server's users.
    /// </summary>
    [Fact]
    public async Task Redeem_TakenUsername_GetsAGenericReply()
    {
        _h.Users.Add("viewer");

        var result = await _h.RedeemAsync(Make().Token);

        Assert.False(result.Success);
        Assert.DoesNotContain("taken", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The last use writes the used up entry and event.
    /// True: an administrator knows when a link has made all its accounts.
    /// False: a used up invite would go quiet.
    /// </summary>
    [Fact]
    public async Task Redeem_LastUse_ReportsUsedUp()
    {
        var invite = Make(new InviteRequest { MaxUses = 1 });

        await _h.RedeemAsync(invite.Token);

        Assert.Contains("Invite reached its limit", _h.Activity.Names);
        Assert.Contains(_h.Notifier.Events, e => e.Key == InvitesEvents.UsedUp);
        Assert.False((await _h.RedeemAsync(invite.Token, username: "second")).Success);
    }

    /// <summary>
    /// A password Jellyfin refuses removes the half made account, and the use is not counted.
    /// True: the invitee can try again with the same user name.
    /// False: a half made account would keep the name and use up the invite.
    /// </summary>
    [Fact]
    public async Task Redeem_PasswordFails_RollsBack()
    {
        var invite = Make();
        _h.Users.Stub.Answer("ChangePassword", _ => throw new InvalidOperationException("refused"));

        var result = await _h.RedeemAsync(invite.Token);

        Assert.False(result.Success);
        Assert.Empty(_h.Users.Users);
        Assert.Single(_h.Users.Deleted);
        Assert.Null(_h.Status.Find(invite.Id));
    }

    /// <summary>
    /// An administrator making a user while no signup runs still sees the group for new users, with no hold.
    /// True: the invite's hold never leaks onto other accounts.
    /// False: accounts made in the dashboard would be disabled or left groupless.
    /// </summary>
    [Fact]
    public async Task UserMadeOutsideASignup_GetsTheGroupForNewUsersAndNoHold()
    {
        var family = TestGroups.Make("Family");
        _h.Config.Configuration.Groups.Add(family);
        _h.Config.Configuration.GroupSettings.NewUserGroupId = family.Id;

        var user = await _h.Users.Manager.CreateUserAsync("dashboard");

        Assert.Contains(user.Id, family.MemberIds);
        Assert.False(_h.DisabledWhenCreated);
    }
}
