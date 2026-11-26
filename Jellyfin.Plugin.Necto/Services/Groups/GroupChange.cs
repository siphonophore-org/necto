using Jellyfin.Plugin.Necto.Configuration.Groups;

namespace Jellyfin.Plugin.Necto.Services.Groups;

/// <summary>
/// The result of a change to a group: the group as saved, or why the change was refused.
/// </summary>
/// <param name="Group">The group as saved, or <c>null</c> when refused or deleted.</param>
/// <param name="Error">Why the change was refused, or None.</param>
public sealed record GroupChange(Group? Group, GroupChangeError Error)
{
    /// <summary>Gets a value indicating whether the change was saved.</summary>
    public bool Succeeded => Error == GroupChangeError.None;

    /// <summary>
    /// Builds a refused change.
    /// </summary>
    /// <param name="error">Why it was refused.</param>
    /// <returns>The result.</returns>
    public static GroupChange Refused(GroupChangeError error) => new(null, error);
}
