using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Accounts;
using Jellyfin.Plugin.Necto.Configuration.Groups;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Configuration;

/// <summary>
/// A fresh install has Everyone, no groups, no group for new users, reset codes hidden, and every rule off.
/// </summary>
public class PluginConfigurationTests
{
    /// <summary>
    /// The defaults give nothing and enforce nothing.
    /// True: installing Necto changes no user's access or account.
    /// False: a new install would hold users to rules no one set.
    /// </summary>
    [Fact]
    public void Defaults_GiveNothingAndEnforceNothing()
    {
        var config = new PluginConfiguration();

        Assert.Empty(config.Groups);
        Assert.Equal(Group.EveryoneId, config.Everyone.Id);
        Assert.True(config.Everyone.IsEveryone);
        Assert.Null(config.GroupSettings.NewUserGroupId);
        Assert.False(config.AccountSettings.ShowResetCodes);
        var accounts = config.Everyone.Permissions.Accounts;
        Assert.False(accounts.Password.Enabled);
        Assert.True(accounts.Password.DisallowEmpty);
        Assert.Equal(8, accounts.Password.MinLength);
        Assert.Equal(PasswordChangeMode.Allowed, accounts.Password.ChangeMode);
        Assert.Null(accounts.Expiry.ExpiresOn);
        Assert.Equal(ExpiryAction.Disable, accounts.Expiry.Action);
        Assert.False(accounts.Inactivity.Enabled);
        Assert.Equal(30, accounts.Inactivity.Days);
        Assert.False(accounts.Sessions.Enabled);
        Assert.False(config.Everyone.Permissions.Requests.MayRequest);
    }
}
