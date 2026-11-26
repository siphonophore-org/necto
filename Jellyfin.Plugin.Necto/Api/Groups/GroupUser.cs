using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Necto.Api.Groups;

/// <summary>
/// A Jellyfin user and the groups that apply to them, for the Groups page.
/// </summary>
public sealed class GroupUser
{
    /// <summary>Gets or sets the user's id.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the user's name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the user is an administrator, who no group holds.</summary>
    public bool IsAdministrator { get; set; }

    /// <summary>Gets or sets a value indicating whether the account is disabled.</summary>
    public bool IsDisabled { get; set; }

    /// <summary>Gets or sets the tag of the user's picture, or <c>null</c> when they have none.</summary>
    public string? PrimaryImageTag { get; set; }

    /// <summary>Gets or sets the groups the user is in, or Everyone's id when they are in none. Empty for an administrator.</summary>
    public List<Guid> GroupIds { get; set; } = new();
}
