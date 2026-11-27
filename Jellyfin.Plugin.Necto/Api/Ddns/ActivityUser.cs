using System;
using System.Security.Claims;
using Siphonophore.Stolon;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Services.Proxy.Reachability;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;

namespace Jellyfin.Plugin.Necto.Api.Ddns;

/// <summary>
/// The Jellyfin user behind a request, so an activity entry for an administrator's change says who made it.
/// </summary>
public static class ActivityUser
{
    // The claim Jellyfin's authentication puts the user's id in.
    private const string UserIdClaim = "Jellyfin-UserId";

    /// <summary>
    /// The id of the signed-in user.
    /// </summary>
    /// <param name="user">The request's user.</param>
    /// <returns>The id, or empty when the request carries none, such as one made with an API key.</returns>
    public static Guid Of(ClaimsPrincipal? user)
        => Guid.TryParse(user?.FindFirst(UserIdClaim)?.Value, out var id) ? id : Guid.Empty;

    /// <summary>
    /// The name of the signed-in user, for an activity overview that says who made the change.
    /// </summary>
    /// <param name="user">The request's user.</param>
    /// <returns>The user's name, or "An administrator" in the server's language when the request carries none.</returns>
    public static string NameOf(ClaimsPrincipal? user)
    {
        var name = user?.FindFirst(ClaimTypes.Name)?.Value;
        return string.IsNullOrWhiteSpace(name) ? ServerText.Get(TranslationKeys.ActivityAnAdministrator) : name;
    }
}
