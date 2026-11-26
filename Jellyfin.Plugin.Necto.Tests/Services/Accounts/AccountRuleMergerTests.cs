using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.Accounts;
using Jellyfin.Plugin.Necto.Configuration.Groups;
using Jellyfin.Plugin.Necto.Services.Accounts;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Accounts;

/// <summary>
/// Account rules merge across a user's groups: password rules from any group at their strictest, with the most
/// generous change mode, and expiry, inactivity, and session cleanup only when every group has them, at the largest
/// window.
/// </summary>
public class AccountRuleMergerTests
{
    private static readonly DateTime Today = new(2026, 6, 15);

    private static Group Expiring(DateTime? on, ExpiryAction action = ExpiryAction.Disable)
        => TestGroups.Make("G", p => { p.Accounts.Expiry.ExpiresOn = on; p.Accounts.Expiry.Action = action; });

    private static Group Cleaning(params SessionCleanupRule[] rules)
        => TestGroups.Make("S", p => { p.Accounts.Sessions.Enabled = true; p.Accounts.Sessions.Rules.AddRange(rules); });

    /// <summary>
    /// No group enforcing password rules leaves the user free.
    /// True: a user outside every enforcing group sets any password.
    /// False: a group with rules turned off would still hold its members.
    /// </summary>
    [Fact]
    public void Password_NoGroupEnforces_IsNull()
    {
        Assert.Null(AccountRuleMerger.Password(new[] { TestGroups.Make("A"), TestGroups.Make("B") }));
    }

    /// <summary>
    /// The rules of the enforcing groups merge at their strictest, with the most generous change mode.
    /// True: a member of two enforcing groups meets both groups' requirements and gets the freer change mode.
    /// False: a second group could weaken a requirement, or block a change the first allows.
    /// </summary>
    [Fact]
    public void Password_EnforcingGroups_MergeStrictestRulesAndFreestChangeMode()
    {
        var a = TestGroups.Make("A", p => { var r = p.Accounts.Password; r.Enabled = true; r.MinLength = 6; r.RequireNumber = true; r.DisallowEmpty = false; r.ChangeMode = PasswordChangeMode.Disallowed; });
        var b = TestGroups.Make("B", p => { var r = p.Accounts.Password; r.Enabled = true; r.MinLength = 10; r.RequireSymbol = true; r.DisallowEmpty = false; r.ChangeMode = PasswordChangeMode.InitialOnly; });
        var off = TestGroups.Make("Off", p => { var r = p.Accounts.Password; r.MinLength = 40; r.RequireUppercase = true; });

        var rules = AccountRuleMerger.Password(new[] { a, b, off })!;

        Assert.True(rules.Enabled);
        Assert.Equal(10, rules.MinLength);
        Assert.True(rules.RequireNumber);
        Assert.True(rules.RequireSymbol);
        Assert.False(rules.RequireUppercase);
        Assert.False(rules.DisallowEmpty);
        Assert.Equal(PasswordChangeMode.InitialOnly, rules.ChangeMode);
    }

    /// <summary>
    /// A user expires only once every group has expired.
    /// True: a group with no expiry keeps the member's account.
    /// False: an expired group would disable a member another group still holds.
    /// </summary>
    [Fact]
    public void Expiry_OneGroupNeverExpires_KeepsTheAccount()
    {
        Assert.Null(AccountRuleMerger.Expiry(new[] { Expiring(Today.AddDays(-3)), Expiring(null) }, Today));
    }

    /// <summary>
    /// The account expires on the latest date of its groups, counted by day.
    /// True: a member expires once the last of their groups does, from that day on.
    /// False: the first group to expire would end the account.
    /// </summary>
    [Theory]
    [InlineData(-1, -5, ExpiryAction.Disable)]
    [InlineData(0, -5, ExpiryAction.Disable)]
    [InlineData(1, -5, null)]
    public void Expiry_EveryGroupHasADate_ExpiresOnTheLatest(int latest, int earliest, ExpiryAction? expected)
    {
        var groups = new[] { Expiring(Today.AddDays(earliest)), Expiring(Today.AddDays(latest).AddHours(15)) };

        Assert.Equal(expected, AccountRuleMerger.Expiry(groups, Today.AddHours(9)));
        Assert.Equal(Today.AddDays(latest), AccountRuleMerger.ExpiresOn(groups));
    }

    /// <summary>
    /// Delete takes every expired group asking for it.
    /// True: an account is deleted only when no group would only disable it.
    /// False: one group could delete an account another group only disables.
    /// </summary>
    [Theory]
    [InlineData(ExpiryAction.Delete, ExpiryAction.Delete, ExpiryAction.Delete)]
    [InlineData(ExpiryAction.Delete, ExpiryAction.Disable, ExpiryAction.Disable)]
    [InlineData(ExpiryAction.Disable, ExpiryAction.Disable, ExpiryAction.Disable)]
    public void Expiry_Delete_TakesEveryGroup(ExpiryAction first, ExpiryAction second, ExpiryAction expected)
    {
        var groups = new[] { Expiring(Today.AddDays(-1), first), Expiring(Today.AddDays(-2), second) };

        Assert.Equal(expected, AccountRuleMerger.Expiry(groups, Today));
    }

    /// <summary>
    /// Inactivity holds only when every group has it, at the largest window.
    /// True: a member is disabled only after the longest window any of their groups gives.
    /// False: a member would be disabled sooner than one of their groups allows.
    /// </summary>
    [Fact]
    public void InactiveDays_EveryGroupHasIt_TakesTheLargest()
    {
        var a = TestGroups.Make("A", p => { p.Accounts.Inactivity.Enabled = true; p.Accounts.Inactivity.Days = 30; });
        var b = TestGroups.Make("B", p => { p.Accounts.Inactivity.Enabled = true; p.Accounts.Inactivity.Days = 90; });
        var off = TestGroups.Make("Off");

        Assert.Equal(90, AccountRuleMerger.InactiveDays(new[] { a, b }));
        Assert.Null(AccountRuleMerger.InactiveDays(new[] { a, off }));
    }

    /// <summary>
    /// A rule's mode decides which clients it covers, whatever their case.
    /// True: Only and All except read the client list as the dashboard shows it.
    /// False: a rule would log out the wrong clients.
    /// </summary>
    [Theory]
    [InlineData(SessionClientMode.All, "Swiftfin", true)]
    [InlineData(SessionClientMode.Only, "Swiftfin", true)]
    [InlineData(SessionClientMode.Only, "Infuse", false)]
    [InlineData(SessionClientMode.Only, "swiftfin", true)]
    [InlineData(SessionClientMode.AllExcept, "Swiftfin", false)]
    [InlineData(SessionClientMode.AllExcept, "Infuse", true)]
    [InlineData(SessionClientMode.AllExcept, "swiftfin", false)]
    public void RuleMatches_ByMode(SessionClientMode mode, string appName, bool expected)
    {
        var rule = new SessionCleanupRule { Mode = mode, Clients = new List<string> { "Swiftfin" } };

        Assert.Equal(expected, AccountRuleMerger.RuleMatches(rule, appName));
    }

    /// <summary>
    /// Within one group the shortest window of the rules that cover a client wins.
    /// True: a specific rule can log a client out sooner than a catch all rule.
    /// False: overlapping rules would keep a client longer than the group meant.
    /// </summary>
    [Fact]
    public void MinMatchingDays_OverlappingRules_ShortestWins()
    {
        var rules = new[]
        {
            new SessionCleanupRule { Mode = SessionClientMode.All, Days = 90 },
            new SessionCleanupRule { Mode = SessionClientMode.Only, Clients = { "Jellyfin Web" }, Days = 7 }
        };

        Assert.Equal(7, AccountRuleMerger.MinMatchingDays(rules, "Jellyfin Web"));
        Assert.Equal(90, AccountRuleMerger.MinMatchingDays(rules, "Infuse"));
        Assert.Null(AccountRuleMerger.MinMatchingDays(new[] { new SessionCleanupRule { Mode = SessionClientMode.Only, Clients = { "Infuse" } } }, "Swiftfin"));
    }

    /// <summary>
    /// Across groups a client is cleaned only when every group cleans it, after the largest window.
    /// True: a member's device stays as long as any of their groups lets it.
    /// False: one group's short window would log out a device another group keeps.
    /// </summary>
    [Fact]
    public void SessionDays_AcrossGroups_EveryGroupAndTheLargestWindow()
    {
        var week = Cleaning(new SessionCleanupRule { Days = 7 });
        var month = Cleaning(new SessionCleanupRule { Days = 30 });
        var onlyInfuse = Cleaning(new SessionCleanupRule { Mode = SessionClientMode.Only, Clients = { "Infuse" }, Days = 1 });

        Assert.Equal(30, AccountRuleMerger.SessionDays(new[] { week, month }, "Swiftfin"));
        Assert.Null(AccountRuleMerger.SessionDays(new[] { week, onlyInfuse }, "Swiftfin"));
        Assert.Null(AccountRuleMerger.SessionDays(new[] { week, TestGroups.Make("Off") }, "Swiftfin"));
        Assert.True(AccountRuleMerger.CleansSessions(new[] { week, month }));
        Assert.False(AccountRuleMerger.CleansSessions(new[] { week, TestGroups.Make("Off") }));
    }
}
