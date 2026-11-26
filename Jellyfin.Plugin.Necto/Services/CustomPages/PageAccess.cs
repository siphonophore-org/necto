using System;
using Jellyfin.Plugin.Necto.Configuration.CustomPages;
using Jellyfin.Plugin.Necto.Services.Groups;

namespace Jellyfin.Plugin.Necto.Services.CustomPages;

/// <summary>
/// Decides who may open a page. An Anyone page opens for everyone, an Administrators page for administrators, and a
/// Signed in page for administrators and for users whose groups give it. This is the only place the rule lives, so
/// the content routes and the server side routes cannot drift apart.
/// </summary>
public static class PageAccess
{
    /// <summary>
    /// Works out whether a caller may open a page.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="caller">What the caller may do, from <see cref="PermissionService"/>, or <c>null</c> for a caller with no user.</param>
    /// <returns><c>true</c> when the caller may open the page.</returns>
    public static bool MayOpen(CustomPage page, EffectivePermissions? caller)
    {
        ArgumentNullException.ThrowIfNull(page);

        // A tier no enum value names opens for nobody, since it compares above every tier.
        if (!Enum.IsDefined(page.Visibility))
        {
            return false;
        }

        if (page.Visibility == PageVisibility.Anonymous)
        {
            return true;
        }

        if (caller is null || !caller.Exists || caller.UserId == Guid.Empty)
        {
            return false;
        }

        return page.Visibility == PageVisibility.Admin
            ? caller.IsAdministrator
            : caller.MayOpenPage(page.Id);
    }
}
