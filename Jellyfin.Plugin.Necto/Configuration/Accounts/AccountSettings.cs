namespace Jellyfin.Plugin.Necto.Configuration.Accounts;

/// <summary>
/// The account settings that are not permissions.
/// </summary>
public class AccountSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether a stored configuration listed every reset code. The group normalizer moves
    /// it onto each group's <see cref="ResetRules.ShowCodes"/> and clears it.
    /// </summary>
    public bool ShowResetCodes { get; set; }
}
