using System;

namespace Jellyfin.Plugin.Necto.Services.Requests;

/// <summary>
/// The settings and lookups a request's or an issue's events are built with, so building them needs no server.
/// </summary>
/// <param name="AutoDeclineAfterDays">After how many days a waiting request is rejected, for an expiring one.</param>
/// <param name="Now">The time now.</param>
/// <param name="ServerUrl">The server's public address posters are made absolute with, or empty.</param>
/// <param name="UserIdByName">Finds a user by name, such as the administrator who decided, or gives <c>null</c>.</param>
/// <param name="SeesRequesters">Tells whether a user may see who made a request.</param>
internal sealed record NoticeContext(int AutoDeclineAfterDays, DateTime Now, string ServerUrl, Func<string?, Guid?> UserIdByName, Func<Guid, bool> SeesRequesters)
{
    /// <summary>
    /// Builds a context with no users to look up, as a test or a preview has.
    /// </summary>
    /// <param name="autoDeclineAfterDays">After how many days a waiting request is rejected.</param>
    /// <param name="now">The time now.</param>
    /// <param name="serverUrl">The server's public address, or empty.</param>
    /// <returns>The context.</returns>
    public static NoticeContext Plain(int autoDeclineAfterDays, DateTime now, string serverUrl = "")
        => new(autoDeclineAfterDays, now, serverUrl, _ => null, _ => false);
}
