using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Services.Groups;

namespace Jellyfin.Plugin.Necto.Services.Accounts;

/// <summary>
/// Picks the password resets Troubleshooting lists: those of members whose group shows reset codes. Administrators are
/// in no group, so their codes never show.
/// </summary>
public static class ResetCodeFilter
{
    /// <summary>
    /// Tells whether any group shows reset codes.
    /// </summary>
    /// <param name="config">The configuration that holds the groups.</param>
    /// <returns><c>true</c> when at least one group shows them.</returns>
    public static bool AnyShown(PluginConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return config.AccountSettings?.ShowResetCodes == true
            || (config.Groups ?? new()).Append(config.Everyone).Any(g => g?.Permissions?.Accounts?.Resets?.ShowCodes == true);
    }

    /// <summary>
    /// Keeps the resets of members whose group shows reset codes.
    /// </summary>
    /// <param name="config">The configuration that holds the groups.</param>
    /// <param name="codes">Every pending reset.</param>
    /// <param name="findUser">Finds a user by name, giving the id and whether they are an administrator, or <c>null</c> when there is no such user.</param>
    /// <returns>The resets to list, in the order given.</returns>
    public static IReadOnlyList<ResetCode> Shown(PluginConfiguration config, IEnumerable<ResetCode> codes, Func<string, (Guid Id, bool IsAdministrator)?> findUser)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(codes);
        ArgumentNullException.ThrowIfNull(findUser);

        // A stored configuration that still lists every code shows each member's.
        var all = config.AccountSettings?.ShowResetCodes == true;
        return codes.Where(code =>
        {
            if (findUser(code.UserName) is not { } user || user.IsAdministrator)
            {
                return false;
            }

            return all || GroupMembership.GroupOf(config, user.Id).Permissions?.Accounts?.Resets?.ShowCodes == true;
        }).ToList();
    }
}
