namespace Jellyfin.Plugin.Necto.Configuration.Accounts;

/// <summary>
/// Disables members who stop using the server. A member who never signed in is left alone.
/// </summary>
public class InactivityRule
{
    /// <summary>Gets or sets a value indicating whether inactive members are disabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets how many days without activity disable a member.</summary>
    public int Days { get; set; } = 30;
}
