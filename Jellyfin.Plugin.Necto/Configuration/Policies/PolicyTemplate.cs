namespace Jellyfin.Plugin.Necto.Configuration.Policies;

/// <summary>
/// The Jellyfin policy and preferences a group writes onto its members. A member takes the template of the first
/// group, in group order, that has one turned on.
/// </summary>
public class PolicyTemplate
{
    /// <summary>Gets or sets a value indicating whether the group writes its template onto its members.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the Access and features template.</summary>
    public AccessTemplate Access { get; set; } = new();

    /// <summary>Gets or sets the Preferences template.</summary>
    public PreferencesTemplate Preferences { get; set; } = new();
}
