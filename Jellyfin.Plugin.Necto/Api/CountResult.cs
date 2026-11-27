namespace Jellyfin.Plugin.Necto.Api;

/// <summary>
/// How many things an action changed, such as the entries a clear removed.
/// </summary>
/// <param name="Count">The count.</param>
public sealed record CountResult(int Count);
