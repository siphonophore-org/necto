using System;
using Jellyfin.Plugin.Necto.Configuration.CustomPages;

namespace Jellyfin.Plugin.Necto.Services.CustomPages;

/// <summary>
/// The identity behind a store request, reduced to the three things the access rules care about.
/// </summary>
/// <param name="Tier">The highest tier the caller satisfies.</param>
/// <param name="UserId">The caller's user ID, or <see cref="Guid.Empty"/> when it has no user identity.</param>
public readonly record struct StoreCaller(PageVisibility Tier, Guid UserId)
{
    /// <summary>Gets a caller with no credentials at all.</summary>
    public static StoreCaller Anonymous => new StoreCaller(PageVisibility.Anonymous, Guid.Empty);

    /// <summary>Gets a value indicating whether the caller can own records.</summary>
    public bool HasIdentity => UserId != Guid.Empty;
}
