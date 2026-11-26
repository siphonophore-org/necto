namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// What came of trying to mount a missing disk again.
/// </summary>
/// <param name="Tried">Whether the plugin knows how to mount this disk. A disk it does not is left alone.</param>
/// <param name="Mounted">Whether the mount call worked.</param>
/// <param name="Failure">Why it did not, when it did not.</param>
/// <param name="Code">The OS's error code, for the server log.</param>
public sealed record RemountResult(bool Tried, bool Mounted, RemountFailure Failure = RemountFailure.Other, long Code = 0)
{
    /// <summary>Gets the result for a disk the plugin does not know how to mount.</summary>
    public static RemountResult Unsupported { get; } = new(false, false);

    /// <summary>Gets the result of a mount call that worked.</summary>
    public static RemountResult Success { get; } = new(true, true);

    /// <summary>
    /// The result of a mount call that failed.
    /// </summary>
    /// <param name="failure">Why.</param>
    /// <param name="code">The OS's error code.</param>
    /// <returns>The result.</returns>
    public static RemountResult Failed(RemountFailure failure, long code = 0) => new(true, false, failure, code);
}
