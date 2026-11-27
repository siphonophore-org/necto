namespace Jellyfin.Plugin.Necto.Configuration.Policies;

/// <summary>
/// The Jellyfin policy and preferences a group writes onto its members. Each row is set by the group or left to the
/// member, and a member of several groups takes each row from the first group, in group order, that sets it.
/// </summary>
public class PolicyTemplate
{
    /// <summary>
    /// Gets or sets a value indicating whether the rows set by the group apply. False marks a stored template whose
    /// rows are all off, and normalizing it clears every row and sets this back to <c>true</c>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the Access and features template.</summary>
    public AccessTemplate Access { get; set; } = new();

    /// <summary>Gets or sets the Preferences template.</summary>
    public PreferencesTemplate Preferences { get; set; } = new();
}
