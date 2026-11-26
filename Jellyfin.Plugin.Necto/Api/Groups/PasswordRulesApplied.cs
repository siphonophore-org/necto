namespace Jellyfin.Plugin.Necto.Api.Groups;

/// <summary>
/// What applying the password rules changed.
/// </summary>
/// <param name="Moved">How many users moved onto or off password rules.</param>
public sealed record PasswordRulesApplied(int Moved);
