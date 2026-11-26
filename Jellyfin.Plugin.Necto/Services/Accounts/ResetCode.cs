using System;

namespace Jellyfin.Plugin.Necto.Services.Accounts;

/// <summary>
/// A pending password reset, read from a file Jellyfin's own reset provider writes.
/// </summary>
public sealed class ResetCode
{
    /// <summary>Gets or sets the user the reset is for.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Gets or sets the code the user enters.</summary>
    public string Pin { get; set; } = string.Empty;

    /// <summary>Gets or sets when the code stops working.</summary>
    public DateTime ExpirationDate { get; set; }

    /// <summary>Gets or sets a value indicating whether the code has stopped working.</summary>
    public bool Expired { get; set; }
}
