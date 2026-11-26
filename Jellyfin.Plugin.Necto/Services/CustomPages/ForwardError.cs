namespace Jellyfin.Plugin.Necto.Services.CustomPages;

/// <summary>
/// Why a route could not be forwarded, when it could not.
/// </summary>
public enum ForwardError
{
    /// <summary>The target answered and its response is relayed.</summary>
    None,

    /// <summary>The target could not be reached, or the connection failed mid response.</summary>
    Unreachable,

    /// <summary>The target did not answer within the route timeout.</summary>
    TimedOut,

    /// <summary>The target answered with more than <see cref="RouteProxy.MaxResponseBytes"/>.</summary>
    ResponseTooLarge
}
