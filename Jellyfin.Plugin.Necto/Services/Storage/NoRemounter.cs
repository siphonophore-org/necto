using Jellyfin.Plugin.Necto.Configuration.Storage;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// The remounter on an OS the plugin has none for. No disk is mounted again.
/// </summary>
public sealed class NoRemounter : IRemounter
{
    /// <inheritdoc />
    public bool TakesAddress => false;

    /// <inheritdoc />
    public string? SourceOf(string mountPoint) => null;

    /// <inheritdoc />
    public RemountResult Remount(string mountPoint, string? source) => RemountResult.Unsupported;
}
