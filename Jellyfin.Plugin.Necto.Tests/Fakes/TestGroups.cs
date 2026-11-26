using System;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Groups;

namespace Jellyfin.Plugin.Necto.Tests.Fakes;

/// <summary>
/// Builds groups for tests, each with a new id and the given members.
/// </summary>
public static class TestGroups
{
    public static Group Make(string name, params Guid[] members)
        => new() { Id = Guid.NewGuid(), Name = name, MemberIds = members.ToList() };

    public static Group Make(string name, Action<GroupPermissions> permissions, params Guid[] members)
    {
        var group = Make(name, members);
        permissions(group.Permissions);
        return group;
    }

    public static PluginConfiguration Config(params Group[] groups)
        => new() { Groups = groups.ToList() };
}
