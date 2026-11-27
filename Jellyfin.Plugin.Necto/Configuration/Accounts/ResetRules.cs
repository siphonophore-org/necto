namespace Jellyfin.Plugin.Necto.Configuration.Accounts;

/// <summary>
/// The password reset rules a group applies to its members.
/// </summary>
public class ResetRules
{
    /// <summary>
    /// Gets or sets a value indicating whether Troubleshooting lists the codes of the members' pending password resets.
    /// Off by default, since over plain HTTP a code can be read in transit.
    /// </summary>
    public bool ShowCodes { get; set; }
}
