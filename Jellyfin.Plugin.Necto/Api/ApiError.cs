namespace Jellyfin.Plugin.Necto.Api;

/// <summary>
/// The body of a refused request, which the page shows as is.
/// </summary>
/// <param name="Error">One sentence on what is wrong.</param>
public sealed record ApiError(string Error);
