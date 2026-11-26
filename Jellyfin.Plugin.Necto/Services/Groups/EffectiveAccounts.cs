using System;
using Jellyfin.Plugin.Necto.Configuration.Accounts;

namespace Jellyfin.Plugin.Necto.Services.Groups;

/// <summary>
/// The account rules that hold for one user, merged from the groups that apply to them.
/// </summary>
public sealed class EffectiveAccounts
{
    /// <summary>Gets or sets the password rules the user is held to, or <c>null</c> for none.</summary>
    public PasswordRules? Password { get; set; }

    /// <summary>Gets or sets the date the account expires on, or <c>null</c> for never.</summary>
    public DateTime? ExpiresOn { get; set; }

    /// <summary>Gets or sets what expiry does to the account, or <c>null</c> when it never expires.</summary>
    public ExpiryAction? ExpiryAction { get; set; }

    /// <summary>Gets or sets how many days without activity disable the account, or <c>null</c> for never.</summary>
    public int? InactiveDays { get; set; }

    /// <summary>Gets or sets a value indicating whether the user's stale devices are logged out.</summary>
    public bool CleansSessions { get; set; }
}
