using System;
using Jellyfin.Plugin.Necto.Configuration.Policies;
using Jellyfin.Plugin.Necto.Services.Policies;
using MediaBrowser.Model.Users;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Policies;

/// <summary>
/// An Access and features template overwrites only the policy values it manages, and leaves the rest as the member has them.
/// </summary>
public class AccessTemplateExtensionsTests
{
    /// <summary>
    /// Merge unmanaged permission leaves policy untouched.
    /// True: the template writes only the values it manages, as User Management did.
    /// False: a member's own setting would be lost, or a managed one ignored.
    /// </summary>
    [Fact]
    public void Merge_UnmanagedPermission_LeavesPolicyUntouched()
    {
        var policy = new UserPolicy { EnableRemoteAccess = false };
        var perms = new AccessTemplate { ManageEnableRemoteAccess = false, EnableRemoteAccess = true };

        perms.ApplyTo(policy, Guid.NewGuid());

        Assert.False(policy.EnableRemoteAccess);
    }

    /// <summary>
    /// Merge managed permission overwrites policy.
    /// True: the template writes only the values it manages, as User Management did.
    /// False: a member's own setting would be lost, or a managed one ignored.
    /// </summary>
    [Fact]
    public void Merge_ManagedPermission_OverwritesPolicy()
    {
        var policy = new UserPolicy { EnableRemoteAccess = true };
        var perms = new AccessTemplate { ManageEnableRemoteAccess = true, EnableRemoteAccess = false };

        perms.ApplyTo(policy, Guid.NewGuid());

        Assert.False(policy.EnableRemoteAccess);
    }

    /// <summary>
    /// Merge managed user preference access overwrites policy.
    /// True: the template writes only the values it manages, as User Management did.
    /// False: a member's own setting would be lost, or a managed one ignored.
    /// </summary>
    [Fact]
    public void Merge_ManagedUserPreferenceAccess_OverwritesPolicy()
    {
        var policy = new UserPolicy { EnableUserPreferenceAccess = true };
        var perms = new AccessTemplate { ManageEnableUserPreferenceAccess = true, EnableUserPreferenceAccess = false };

        perms.ApplyTo(policy, Guid.NewGuid());

        Assert.False(policy.EnableUserPreferenceAccess);
    }

    /// <summary>
    /// Merge managed sync transcoding overwrites policy.
    /// True: the template writes only the values it manages, as User Management did.
    /// False: a member's own setting would be lost, or a managed one ignored.
    /// </summary>
    [Fact]
    public void Merge_ManagedSyncTranscoding_OverwritesPolicy()
    {
        var policy = new UserPolicy { EnableSyncTranscoding = true };
        var perms = new AccessTemplate { ManageEnableSyncTranscoding = true, EnableSyncTranscoding = false };

        perms.ApplyTo(policy, Guid.NewGuid());

        Assert.False(policy.EnableSyncTranscoding);
    }

    /// <summary>
    /// Merge managed public sharing overwrites policy.
    /// True: the template writes only the values it manages, as User Management did.
    /// False: a member's own setting would be lost, or a managed one ignored.
    /// </summary>
    [Fact]
    public void Merge_ManagedPublicSharing_OverwritesPolicy()
    {
        var policy = new UserPolicy { EnablePublicSharing = true };
        var perms = new AccessTemplate { ManageEnablePublicSharing = true, EnablePublicSharing = false };

        perms.ApplyTo(policy, Guid.NewGuid());

        Assert.False(policy.EnablePublicSharing);
    }

    /// <summary>
    /// Merge unmanaged new permissions leave policy untouched.
    /// True: the template writes only the values it manages, as User Management did.
    /// False: a member's own setting would be lost, or a managed one ignored.
    /// </summary>
    [Fact]
    public void Merge_UnmanagedNewPermissions_LeavePolicyUntouched()
    {
        var policy = new UserPolicy { EnableUserPreferenceAccess = false, EnableSyncTranscoding = false, EnablePublicSharing = false };
        var perms = new AccessTemplate();

        perms.ApplyTo(policy, Guid.NewGuid());

        Assert.False(policy.EnableUserPreferenceAccess);
        Assert.False(policy.EnableSyncTranscoding);
        Assert.False(policy.EnablePublicSharing);
    }

    /// <summary>
    /// Merge managed library access applies folders and flag.
    /// True: the template writes only the values it manages, as User Management did.
    /// False: a member's own setting would be lost, or a managed one ignored.
    /// </summary>
    [Fact]
    public void Merge_ManagedLibraryAccess_AppliesFoldersAndFlag()
    {
        var folder = Guid.NewGuid();
        var policy = new UserPolicy { EnableAllFolders = true };
        var perms = new AccessTemplate
        {
            ManageLibraryAccess = true,
            EnableAllFolders = false,
            EnabledFolders = { folder }
        };

        perms.ApplyTo(policy, Guid.NewGuid());

        Assert.False(policy.EnableAllFolders);
        Assert.Equal(new[] { folder }, policy.EnabledFolders);
    }

    /// <summary>
    /// Merge managed access schedule produces schedule for user.
    /// True: the template writes only the values it manages, as User Management did.
    /// False: a member's own setting would be lost, or a managed one ignored.
    /// </summary>
    [Fact]
    public void Merge_ManagedAccessSchedule_ProducesScheduleForUser()
    {
        var userId = Guid.NewGuid();
        var policy = new UserPolicy();
        var perms = new AccessTemplate
        {
            ManageAccessSchedules = true,
            AccessSchedules = { new AccessScheduleEntry { DayOfWeek = "Everyday", StartHour = 8, EndHour = 22 } }
        };

        perms.ApplyTo(policy, userId);

        var schedule = Assert.Single(policy.AccessSchedules);
        Assert.Equal(8, schedule.StartHour);
        Assert.Equal(22, schedule.EndHour);
        Assert.Equal(userId, schedule.UserId);
    }

    /// <summary>
    /// Merge block unrated items keeps only parseable names.
    /// True: the template writes only the values it manages, as User Management did.
    /// False: a member's own setting would be lost, or a managed one ignored.
    /// </summary>
    [Fact]
    public void Merge_BlockUnratedItems_KeepsOnlyParseableNames()
    {
        var policy = new UserPolicy();
        var perms = new AccessTemplate
        {
            ManageBlockUnratedItems = true,
            BlockUnratedItems = { "Movie", "NotARealUnratedItem" }
        };

        perms.ApplyTo(policy, Guid.NewGuid());

        Assert.Single(policy.BlockUnratedItems);
    }
}
