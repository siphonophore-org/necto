namespace Jellyfin.Plugin.Necto.Services.Accounts;

/// <summary>
/// What one run of the account rules changed.
/// </summary>
/// <param name="Disabled">How many users were disabled, for expiry or inactivity.</param>
/// <param name="Deleted">How many users were deleted for expiry.</param>
public sealed record AccountRuleRun(int Disabled, int Deleted);
