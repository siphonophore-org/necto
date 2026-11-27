using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.Invites;

namespace Jellyfin.Plugin.Necto.Services.Invites;

/// <summary>
/// What using an invite did.
/// </summary>
/// <param name="Success">Whether an account was made.</param>
/// <param name="Message">One sentence for the signup page.</param>
/// <param name="Resources">The links to show the new user, empty when no account was made.</param>
public sealed record InviteRedeemResult(bool Success, string Message, IReadOnlyList<InviteResource> Resources)
{
    /// <summary>
    /// Builds a refusal.
    /// </summary>
    /// <param name="message">Why.</param>
    /// <returns>The result.</returns>
    public static InviteRedeemResult Fail(string message) => new(false, message, System.Array.Empty<InviteResource>());
}
