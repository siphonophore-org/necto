namespace Jellyfin.Plugin.Necto.Configuration.Accounts;

/// <summary>
/// What happens to a member once their group's expiry date comes.
/// </summary>
public enum ExpiryAction
{
    /// <summary>Disables the account, which can be undone.</summary>
    Disable,

    /// <summary>Deletes the account and its data for good.</summary>
    Delete
}
