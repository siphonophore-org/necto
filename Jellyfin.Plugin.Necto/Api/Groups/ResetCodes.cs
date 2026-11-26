using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Services.Accounts;

namespace Jellyfin.Plugin.Necto.Api.Groups;

/// <summary>
/// The pending password resets, listed only while Settings shows reset codes.
/// </summary>
public sealed class ResetCodes
{
    /// <summary>Gets or sets a value indicating whether Settings shows reset codes.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the pending resets, empty while codes are not shown.</summary>
    public IReadOnlyList<ResetCode> Codes { get; set; } = new List<ResetCode>();
}
