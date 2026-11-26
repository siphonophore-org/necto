namespace Jellyfin.Plugin.Necto.Configuration.Accounts;

/// <summary>
/// The account settings that are not permissions, in the Groups section of Settings.
/// </summary>
public class AccountSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether Troubleshooting lists the codes of pending password resets. Off by default,
    /// since over plain HTTP a code can be read in transit.
    /// </summary>
    public bool ShowResetCodes { get; set; }
}
