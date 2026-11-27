using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Services.Accounts;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Accounts;

/// <summary>
/// Troubleshooting lists a reset code only for a member whose group shows reset codes.
/// </summary>
public class ResetCodeFilterTests
{
    private static readonly Guid Kid = Guid.NewGuid();
    private static readonly Guid Adult = Guid.NewGuid();
    private static readonly Guid Loner = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();

    private static readonly Dictionary<string, (Guid Id, bool IsAdministrator)> Users = new()
    {
        ["kid"] = (Kid, false),
        ["adult"] = (Adult, false),
        ["loner"] = (Loner, false),
        ["admin"] = (Admin, true)
    };

    private static List<ResetCode> Codes() => new[] { "kid", "adult", "loner", "admin", "gone" }.Select(n => new ResetCode { UserName = n, Pin = "123456" }).ToList();

    private static (Guid Id, bool IsAdministrator)? Find(string name) => Users.TryGetValue(name, out var user) ? user : null;

    private static PluginConfiguration Config()
    {
        var kids = TestGroups.Make("Kids", p => p.Accounts.Resets.ShowCodes = true, Kid);
        var adults = TestGroups.Make("Adults", Adult);
        return TestGroups.Config(kids, adults);
    }

    /// <summary>
    /// Only the codes of members in a group that shows them are listed, never an administrator's or an unknown user's.
    /// True: each group decides whether its members' codes show.
    /// False: one switch would show every member's code, or an administrator's.
    /// </summary>
    [Fact]
    public void Shown_KeepsMembersOfGroupsThatShowCodes()
    {
        var config = Config();

        Assert.True(ResetCodeFilter.AnyShown(config));
        Assert.Equal(new[] { "kid" }, ResetCodeFilter.Shown(config, Codes(), Find).Select(c => c.UserName));

        config.Everyone.Permissions.Accounts.Resets.ShowCodes = true;
        Assert.Equal(new[] { "kid", "loner" }, ResetCodeFilter.Shown(config, Codes(), Find).Select(c => c.UserName));
    }

    /// <summary>
    /// With no group showing codes nothing is listed, and a stored setting that listed every code still lists each member's.
    /// True: codes stay hidden by default, and an older setting keeps working until it is moved onto the groups.
    /// False: codes would show with every group off, or vanish for a server that had them on.
    /// </summary>
    [Fact]
    public void AnyShown_FollowsTheGroupsAndTheStoredSetting()
    {
        var config = TestGroups.Config(TestGroups.Make("Adults", Adult));
        Assert.False(ResetCodeFilter.AnyShown(config));
        Assert.Empty(ResetCodeFilter.Shown(config, Codes(), Find));

        config.AccountSettings.ShowResetCodes = true;
        Assert.True(ResetCodeFilter.AnyShown(config));
        Assert.Equal(new[] { "kid", "adult", "loner" }, ResetCodeFilter.Shown(config, Codes(), Find).Select(c => c.UserName));
    }
}
