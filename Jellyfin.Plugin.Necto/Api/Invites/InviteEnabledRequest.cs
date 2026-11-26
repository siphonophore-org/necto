namespace Jellyfin.Plugin.Necto.Api.Invites;

/// <summary>
/// Turns an invite on or off.
/// </summary>
public class InviteEnabledRequest
{
    /// <summary>Gets or sets a value indicating whether the invite works.</summary>
    public bool Enabled { get; set; }
}
