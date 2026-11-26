using Jellyfin.Plugin.Necto.Configuration.CustomPages;

namespace Jellyfin.Plugin.Necto.Services.CustomPages;

/// <summary>
/// Small derived values of <see cref="PageVisibility"/>, so callers read the intent rather than repeat a comparison.
/// </summary>
public static class PageVisibilityExtensions
{
    /// <summary>
    /// Works out whether a tier needs a signed in viewer.
    /// </summary>
    /// <param name="visibility">The tier.</param>
    /// <returns><c>true</c> for User and Admin.</returns>
    public static bool RequiresAuth(this PageVisibility visibility) => visibility != PageVisibility.Anonymous;

    /// <summary>
    /// Names the content route a tier's page is served through.
    /// </summary>
    /// <param name="visibility">The page's tier.</param>
    /// <returns><c>admin</c> for Admin, otherwise <c>user</c>.</returns>
    public static string EndpointTier(this PageVisibility visibility) => visibility == PageVisibility.Admin ? "admin" : "user";

    /// <summary>
    /// Works out whether a viewer cleared for one tier is cleared for content at another. Tiers rise from Anonymous to
    /// User to Admin.
    /// </summary>
    /// <param name="visibility">The tier the viewer is cleared for.</param>
    /// <param name="required">The tier the content needs.</param>
    /// <returns><c>true</c> when this tier is at least the one needed.</returns>
    public static bool Covers(this PageVisibility visibility, PageVisibility required) => visibility >= required;
}
