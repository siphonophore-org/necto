using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Necto.Configuration.Groups;

/// <summary>
/// A named set of Jellyfin users and the permissions they share. A user is in one group at most, and a user in none
/// takes the Everyone group.
/// </summary>
public class Group
{
    /// <summary>
    /// Gets the id of the Everyone group, which always exists and applies to users in no other group.
    /// </summary>
    public static Guid EveryoneId { get; } = Guid.Parse("5a1f0c7e-0000-4000-8000-000000000001");

    /// <summary>Gets or sets the group's id.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the group's name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the Jellyfin ids of the members. Everyone has none, since it holds every user in no group.</summary>
    public List<Guid> MemberIds { get; set; } = new();

    /// <summary>Gets or sets what the group gives its members.</summary>
    public GroupPermissions Permissions { get; set; } = new();

    /// <summary>Gets or sets the Jellyfin policy and preferences the group writes onto its members.</summary>
    public Policies.PolicyTemplate Template { get; set; } = new();

    /// <summary>Gets a value indicating whether this is the Everyone group.</summary>
    public bool IsEveryone => Id == EveryoneId;
}
