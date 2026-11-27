using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Groups;
using Jellyfin.Plugin.Necto.Services.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Groups;

/// <summary>
/// Lists, creates, changes, and deletes groups in the plugin configuration. Every change is normalized against the
/// server's users, so no administrator or deleted user keeps a membership, and creating or deleting a group writes
/// an activity entry.
/// </summary>
[PluginService(ServiceLifetime.Singleton)]
public sealed class GroupStore
{
    private readonly IConfigurationStore _store;
    private readonly IUserManager _userManager;
    private readonly ActivityLogger _activity;
    private readonly IServerConfigurationManager _serverConfig;
    private readonly ILogger<GroupStore> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GroupStore"/> class.
    /// </summary>
    /// <param name="store">Reads and saves the configuration.</param>
    /// <param name="userManager">Lists the server's users, to keep membership to users that exist and are not administrators.</param>
    /// <param name="activity">Writes the activity entries.</param>
    /// <param name="serverConfig">Gives the server's language for the activity entries.</param>
    /// <param name="logger">Writes the server log.</param>
    public GroupStore(IConfigurationStore store, IUserManager userManager, ActivityLogger activity, IServerConfigurationManager serverConfig, ILogger<GroupStore> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(userManager);
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(serverConfig);
        ArgumentNullException.ThrowIfNull(logger);
        _store = store;
        _userManager = userManager;
        _activity = activity;
        _serverConfig = serverConfig;
        _logger = logger;
    }

    /// <summary>
    /// Raised after a group is created, changed, or deleted, or a membership is dropped, so a tool that reads groups
    /// can apply them at once.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Lists every group, Everyone first.
    /// </summary>
    /// <returns>Copies of the groups.</returns>
    public IReadOnlyList<Group> List()
        => _store.Read(c => new[] { c.Everyone ?? PluginConfiguration.NewEveryone() }.Concat(c.Groups ?? new List<Group>()).Select(Completed).ToList());

    /// <summary>
    /// Finds a group.
    /// </summary>
    /// <param name="id">The group's id.</param>
    /// <returns>A copy of the group, or <c>null</c> when none has the id.</returns>
    public Group? Get(Guid id) => List().FirstOrDefault(g => g.Id == id);

    /// <summary>
    /// Copies a group with every section filled in, as a save would leave it.
    /// </summary>
    /// <param name="group">The stored group.</param>
    /// <returns>The copy.</returns>
    private static Group Completed(Group group)
    {
        var copy = SecretJson.Copy(group);
        GroupNormalizer.Complete(copy);
        return copy;
    }

    /// <summary>
    /// Creates a group with a new id.
    /// </summary>
    /// <param name="incoming">The group's name, members, and permissions.</param>
    /// <returns>The group as saved, or why it was refused.</returns>
    public GroupChange Create(Group incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        var group = SecretJson.Copy(incoming);
        group.Id = Guid.NewGuid();
        GroupNormalizer.Complete(group);
        group.Name = group.Name.Trim();

        GroupChange? result = null;
        var (known, admins) = Users();
        _store.Mutate(config =>
        {
            var error = CheckName(config, group.Id, group.Name);
            if (error != GroupChangeError.None)
            {
                result = GroupChange.Refused(error);
                return false;
            }

            config.Groups.Add(group);
            TakeMembers(config, group);
            GroupNormalizer.Normalize(config, known, admins);
            result = new GroupChange(SecretJson.Copy(config.Groups.First(g => g.Id == group.Id)), GroupChangeError.None);
            return true;
        });

        if (result!.Succeeded)
        {
            Changed?.Invoke(this, EventArgs.Empty);
            LogActivity(TranslationKeys.ActivityGroupCreated, TranslationKeys.ActivityGroupCreatedOverview, "Necto.GroupCreated", group.Name);
        }

        return result;
    }

    /// <summary>
    /// Changes a group's name, members, and permissions. Everyone keeps its name and holds no members.
    /// </summary>
    /// <param name="id">The group's id.</param>
    /// <param name="incoming">The group's new name, members, and permissions.</param>
    /// <returns>The group as saved, or why it was refused.</returns>
    public GroupChange Update(Guid id, Group incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        var group = SecretJson.Copy(incoming);
        group.Id = id;
        GroupNormalizer.Complete(group);
        group.Name = group.Name.Trim();

        GroupChange? result = null;
        var (known, admins) = Users();
        _store.Mutate(config =>
        {
            if (id == Group.EveryoneId)
            {
                config.Everyone = group;
            }
            else
            {
                var index = config.Groups.FindIndex(g => g.Id == id);
                if (index < 0)
                {
                    result = GroupChange.Refused(GroupChangeError.NotFound);
                    return false;
                }

                var error = CheckName(config, id, group.Name);
                if (error != GroupChangeError.None)
                {
                    result = GroupChange.Refused(error);
                    return false;
                }

                config.Groups[index] = group;
                TakeMembers(config, group);
            }

            GroupNormalizer.Normalize(config, known, admins);
            var saved = id == Group.EveryoneId ? config.Everyone : config.Groups.First(g => g.Id == id);
            result = new GroupChange(SecretJson.Copy(saved), GroupChangeError.None);
            return true;
        });

        if (result!.Succeeded)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return result;
    }

    /// <summary>
    /// Deletes a group. Its members keep their other groups, and a member in no other group takes Everyone.
    /// </summary>
    /// <param name="id">The group's id.</param>
    /// <returns>None when deleted, or why it was refused.</returns>
    public GroupChange Delete(Guid id)
    {
        if (id == Group.EveryoneId)
        {
            return GroupChange.Refused(GroupChangeError.EveryoneFixed);
        }

        Group? removed = null;
        _store.Mutate(config =>
        {
            removed = config.Groups.FirstOrDefault(g => g.Id == id);
            if (removed is null)
            {
                return false;
            }

            config.Groups.Remove(removed);
            GroupNormalizer.Normalize(config);
            return true;
        });

        if (removed is null)
        {
            return GroupChange.Refused(GroupChangeError.NotFound);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        LogActivity(TranslationKeys.ActivityGroupDeleted, TranslationKeys.ActivityGroupDeletedOverview, "Necto.GroupDeleted", removed.Name);
        return new GroupChange(null, GroupChangeError.None);
    }

    /// <summary>
    /// Drops administrators and deleted users from every group.
    /// </summary>
    /// <returns><c>true</c> when a membership was dropped.</returns>
    public bool CleanMembership()
    {
        var (known, admins) = Users();
        var changed = false;
        var moved = new List<(Guid UserId, string Kept, string Left)>();
        _store.Mutate(config =>
        {
            changed = GroupNormalizer.Normalize(config, known, admins, moved);
            return changed;
        });

        foreach (var (userId, kept, left) in moved)
        {
            _logger.LogInformation("User {UserId} was in several groups and stays in {Kept}, leaving {Left}", userId, FormatUtilities.SanitizeForLog(kept), FormatUtilities.SanitizeForLog(left));
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
            _logger.LogDebug("Dropped administrators and deleted users from group membership");
        }

        return changed;
    }

    /// <summary>
    /// Moves a saved group's members out of every other group, since a user is in one group at most.
    /// </summary>
    /// <param name="config">The configuration, changed in place.</param>
    /// <param name="group">The group just saved.</param>
    private static void TakeMembers(PluginConfiguration config, Group group)
    {
        foreach (var other in config.Groups.Where(g => g.Id != group.Id))
        {
            other.MemberIds.RemoveAll(group.MemberIds.Contains);
        }
    }

    /// <summary>
    /// Finds the reason a name cannot be used, such as when it is blank or another group has it.
    /// </summary>
    private static GroupChangeError CheckName(PluginConfiguration config, Guid id, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return GroupChangeError.NameRequired;
        }

        var taken = string.Equals(name, config.Everyone?.Name, StringComparison.OrdinalIgnoreCase)
            || config.Groups.Any(g => g.Id != id && string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));
        return taken ? GroupChangeError.NameTaken : GroupChangeError.None;
    }

    /// <summary>
    /// Lists the ids of the server's users and of its administrators.
    /// </summary>
    private (List<Guid> Known, List<Guid> Admins) Users()
    {
        var users = _userManager.GetUsers().ToList();
        return (users.Select(u => u.Id).ToList(), users.Where(u => u.HasPermission(PermissionKind.IsAdministrator)).Select(u => u.Id).ToList());
    }

    private void LogActivity(string nameKey, string overviewKey, string type, string groupName)
    {
        var language = StolonTranslations.ServerLanguage(_serverConfig);
        _activity.Log(
            StolonTranslations.Get(nameKey, language),
            type,
            ActivityText.Sentence(StolonTranslations.Format(overviewKey, language, groupName)));
    }
}
