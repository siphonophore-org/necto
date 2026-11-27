using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Configuration.CustomPages;

/// <summary>
/// A named server side route of one page. A call to <c>serverFetch('name', ...)</c> reaches
/// <c>/pages/{slug}/api/{name}</c>, and the server forwards it to <see cref="Url"/> with the route's credentials. The
/// target is fixed here and never comes from the caller, so a page reaches only what an administrator wired up.
/// </summary>
public class PageApiRoute
{
    /// <summary>Gets or sets the route's name, the last path segment and the <c>serverFetch</c> argument.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the absolute address the request is forwarded to.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Gets or sets the HTTP Basic user name sent to the target, or empty.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Gets or sets the HTTP Basic password, stored encrypted, or empty.</summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(KeptSecretConverter))]
    public string Password { get; set; } = string.Empty;
}
