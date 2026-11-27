using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Services.Accounts;

namespace Jellyfin.Plugin.Necto.Api.Groups;

/// <summary>
/// The pending password resets of the members whose group shows reset codes.
/// </summary>
public sealed class ResetCodes
{
    /// <summary>Gets or sets a value indicating whether any group shows reset codes.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the pending resets shown, empty while no group shows codes.</summary>
    public IReadOnlyList<ResetCode> Codes { get; set; } = new List<ResetCode>();
}
