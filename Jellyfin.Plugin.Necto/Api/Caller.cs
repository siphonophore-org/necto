using System;
using System.Security.Claims;

namespace Jellyfin.Plugin.Necto.Api;

/// <summary>
/// Reads who is calling a route from the claims Jellyfin's authentication gives the request.
/// </summary>
public static class Caller
{
    /// <summary>The claim Jellyfin writes the signed in user's id to.</summary>
    public const string UserIdClaim = "Jellyfin-UserId";

    /// <summary>
    /// Reads the caller's user id.
    /// </summary>
    /// <param name="principal">The request's user.</param>
    /// <returns>The id, or <see cref="Guid.Empty"/> for a caller who is not signed in or signed in with an API key.</returns>
    public static Guid UserId(ClaimsPrincipal? principal)
        => Guid.TryParse(principal?.FindFirst(UserIdClaim)?.Value, out var id) ? id : Guid.Empty;
}
