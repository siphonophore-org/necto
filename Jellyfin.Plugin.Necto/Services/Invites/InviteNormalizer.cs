using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Accounts;
using Jellyfin.Plugin.Necto.Configuration.Groups;
using Jellyfin.Plugin.Necto.Configuration.Invites;

namespace Jellyfin.Plugin.Necto.Services.Invites;

/// <summary>
/// Keeps the invites to what Necto relies on: every invite has an id and a token, and an invite whose group
/// disallows password changes is turned off, since a group like that is managed by its administrators.
/// </summary>
public static class InviteNormalizer
{
    /// <summary>
    /// Normalizes the invites in place.
    /// </summary>
    /// <param name="config">The configuration.</param>
    /// <returns>How many invites were turned off for their group.</returns>
    public static int Normalize(PluginConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.InviteSettings ??= new InviteSettings();
        config.InviteSettings.LinkUrl = (config.InviteSettings.LinkUrl ?? string.Empty).Trim().TrimEnd('/');
        config.InviteSettings.RateLimitCount = Math.Max(0, config.InviteSettings.RateLimitCount);
        config.InviteSettings.RateLimitWindowMinutes = Math.Max(0, config.InviteSettings.RateLimitWindowMinutes);
        config.InviteSettings.MaxPinAttempts = Math.Max(1, config.InviteSettings.MaxPinAttempts);

        var seen = new HashSet<Guid>();
        config.Invites = (config.Invites ?? new List<Invite>()).Where(i => i is not null).ToList();
        foreach (var invite in config.Invites)
        {
            if (invite.Id == Guid.Empty || !seen.Add(invite.Id))
            {
                invite.Id = Guid.NewGuid();
                seen.Add(invite.Id);
            }

            if (string.IsNullOrEmpty(invite.Token))
            {
                invite.Token = InviteService.NewToken();
            }

            invite.Label ??= string.Empty;
            invite.Message ??= string.Empty;
            invite.PinHash ??= string.Empty;
            invite.Resources = (invite.Resources ?? new List<InviteResource>()).Where(r => r is not null).ToList();
            invite.MaxUses = Math.Max(0, invite.MaxUses);
        }

        var disabled = 0;
        foreach (var invite in config.Invites.Where(i => i.Enabled))
        {
            if (BlocksInvites(TargetGroup(config, invite)))
            {
                invite.Enabled = false;
                disabled++;
            }
        }

        return disabled;
    }

    /// <summary>
    /// Finds the group a new account would join today: the invite's group, Everyone when the invite names Everyone, or
    /// the group set for new users when the invite names none or its group was deleted. Everyone stands in when neither
    /// is set.
    /// </summary>
    /// <param name="config">The configuration.</param>
    /// <param name="invite">The invite.</param>
    /// <returns>The group. It is Everyone when the account would join no group.</returns>
    public static Group TargetGroup(PluginConfiguration config, Invite invite)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(invite);
        var everyone = config.Everyone ?? PluginConfiguration.NewEveryone();
        if (invite.GroupId == Group.EveryoneId)
        {
            return everyone;
        }

        var groups = config.Groups ?? new List<Group>();
        var own = invite.GroupId is { } id ? groups.FirstOrDefault(g => g.Id == id) : null;
        var fallback = config.GroupSettings?.NewUserGroupId is { } newId ? groups.FirstOrDefault(g => g.Id == newId) : null;
        return own ?? fallback ?? everyone;
    }

    /// <summary>
    /// Works out whether a group cannot take invites, since its members may never set a password.
    /// </summary>
    /// <param name="group">The group.</param>
    /// <returns><c>true</c> when the group enforces password rules that disallow every change.</returns>
    public static bool BlocksInvites(Group? group)
        => group?.Permissions?.Accounts?.Password is { Enabled: true, ChangeMode: PasswordChangeMode.Disallowed };

    /// <summary>
    /// Works out whether a group has expired, so a new account in it would be disabled or deleted at once.
    /// </summary>
    /// <param name="group">The group.</param>
    /// <param name="today">Today, in UTC.</param>
    /// <returns><c>true</c> when the group's expiry date has come.</returns>
    public static bool HasExpired(Group? group, DateTime today)
        => group?.Permissions?.Accounts?.Expiry?.ExpiresOn is { } due && due.Date <= today.Date;
}
