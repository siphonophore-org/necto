using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.Accounts;
using Jellyfin.Plugin.Necto.Configuration.Groups;
using Jellyfin.Plugin.Necto.Services.Accounts;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Accounts;

/// <summary>
/// Account rules come from a user's one group: each rule holds while the group has it on, at the group's own values.
/// </summary>
public class AccountRuleMergerTests
{
    private static readonly DateTime Today = new(2026, 6, 15);

    private static Group Expiring(DateTime? on, ExpiryAction action = ExpiryAction.Disable)
        => TestGroups.Make("G", p => { p.Accounts.Expiry.ExpiresOn = on; p.Accounts.Expiry.Action = action; });

    private static Group Cleaning(params SessionCleanupRule[] rules)
        => TestGroups.Make("S", p => { p.Accounts.Sessions.Enabled = true; p.Accounts.Sessions.Rules.AddRange(rules); });

    /// <summary>
    /// A group with password rules off leaves the user free, whatever values it keeps.
    /// True: a member of a group without rules sets any password.
    /// False: a group with rules turned off would still hold its members.
    /// </summary>
    [Fact]
    public void Password_GroupDoesNotEnforce_IsNull()
    {
        Assert.Null(AccountRuleMerger.Password(TestGroups.Make("A")));
        Assert.Null(AccountRuleMerger.Password(TestGroups.Make("Off", p => { p.Accounts.Password.MinLength = 40; p.Accounts.Password.RequireUppercase = true; })));
    }

    /// <summary>
    /// An enforcing group's rules come back as a copy with every requirement and its change mode.
    /// True: members meet exactly what the group asks.
    /// False: a requirement would be lost, or a change to the copy would reach the stored group.
    /// </summary>
    [Fact]
    public void Password_EnforcingGroup_CopiesItsRules()
    {
        var a = TestGroups.Make("A", p => { var r = p.Accounts.Password; r.Enabled = true; r.MinLength = 10; r.RequireNumber = true; r.RequireSymbol = true; r.DisallowEmpty = false; r.ChangeMode = PasswordChangeMode.InitialOnly; });

        var rules = AccountRuleMerger.Password(a)!;

        Assert.True(rules.Enabled);
        Assert.Equal(10, rules.MinLength);
        Assert.True(rules.RequireNumber);
        Assert.True(rules.RequireSymbol);
        Assert.False(rules.RequireUppercase);
        Assert.False(rules.DisallowEmpty);
        Assert.Equal(PasswordChangeMode.InitialOnly, rules.ChangeMode);
        Assert.NotSame(a.Permissions.Accounts.Password, rules);
    }

    /// <summary>
    /// A group with no expiry date keeps the account.
    /// True: a member of a group that never expires stays.
    /// False: an account would expire with no date set.
    /// </summary>
    [Fact]
    public void Expiry_NoDate_KeepsTheAccount()
    {
        Assert.Null(AccountRuleMerger.Expiry(Expiring(null), Today));
        Assert.Null(AccountRuleMerger.ExpiryActionOf(Expiring(null)));
    }

    /// <summary>
    /// The account expires on the group's date, counted by day.
    /// True: a member expires from that day on, whatever the hour.
    /// False: an account would expire a day early or late.
    /// </summary>
    [Theory]
    [InlineData(-1, ExpiryAction.Disable)]
    [InlineData(0, ExpiryAction.Disable)]
    [InlineData(1, null)]
    public void Expiry_GroupHasADate_ExpiresOnIt(int days, ExpiryAction? expected)
    {
        var group = Expiring(Today.AddDays(days).AddHours(15));

        Assert.Equal(expected, AccountRuleMerger.Expiry(group, Today.AddHours(9)));
        Assert.Equal(Today.AddDays(days), AccountRuleMerger.ExpiresOn(group));
    }

    /// <summary>
    /// Expiry does what the group asks, Delete or Disable.
    /// True: a group set to delete deletes, and any other disables.
    /// False: an account would be deleted when the group only disables.
    /// </summary>
    [Theory]
    [InlineData(ExpiryAction.Delete, ExpiryAction.Delete)]
    [InlineData(ExpiryAction.Disable, ExpiryAction.Disable)]
    public void Expiry_TakesTheGroupsAction(ExpiryAction action, ExpiryAction expected)
    {
        Assert.Equal(expected, AccountRuleMerger.Expiry(Expiring(Today.AddDays(-1), action), Today));
    }

    /// <summary>
    /// Inactivity holds while the group has it on with a window.
    /// True: a member is disabled after the group's window.
    /// False: a group with inactivity off would disable members.
    /// </summary>
    [Fact]
    public void InactiveDays_GroupHasIt_TakesItsWindow()
    {
        var a = TestGroups.Make("A", p => { p.Accounts.Inactivity.Enabled = true; p.Accounts.Inactivity.Days = 90; });
        var zero = TestGroups.Make("Z", p => { p.Accounts.Inactivity.Enabled = true; p.Accounts.Inactivity.Days = 0; });

        Assert.Equal(90, AccountRuleMerger.InactiveDays(a));
        Assert.Null(AccountRuleMerger.InactiveDays(TestGroups.Make("Off")));
        Assert.Null(AccountRuleMerger.InactiveDays(zero));
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
    /// A client is cleaned while the group cleans sessions with a rule that covers it, after that rule's window.
    /// True: a member's device goes after the group's window for its client.
    /// False: a device would be logged out by a group with cleanup off, or by a rule for another client.
    /// </summary>
    [Fact]
    public void SessionDays_TheGroupsRules()
    {
        var week = Cleaning(new SessionCleanupRule { Days = 7 });
        var onlyInfuse = Cleaning(new SessionCleanupRule { Mode = SessionClientMode.Only, Clients = { "Infuse" }, Days = 1 });
        var empty = Cleaning();

        Assert.Equal(7, AccountRuleMerger.SessionDays(week, "Swiftfin"));
        Assert.Null(AccountRuleMerger.SessionDays(onlyInfuse, "Swiftfin"));
        Assert.Equal(1, AccountRuleMerger.SessionDays(onlyInfuse, "Infuse"));
        Assert.Null(AccountRuleMerger.SessionDays(TestGroups.Make("Off"), "Swiftfin"));
        Assert.True(AccountRuleMerger.CleansSessions(week));
        Assert.False(AccountRuleMerger.CleansSessions(TestGroups.Make("Off")));
        Assert.False(AccountRuleMerger.CleansSessions(empty));
    }
}
