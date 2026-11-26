namespace Jellyfin.Plugin.Necto.Services.Notifications;

/// <summary>
/// How much an event matters.
/// </summary>
public enum NectoEventLevel
{
    /// <summary>Something happened as expected.</summary>
    Info,

    /// <summary>Something needs a look.</summary>
    Warning,

    /// <summary>Something failed.</summary>
    Error
}
