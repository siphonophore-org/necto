namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// The answer to a disk call made with a time limit.
/// </summary>
/// <typeparam name="T">What the call returns.</typeparam>
/// <param name="Completed"><c>false</c> when the call did not answer in time.</param>
/// <param name="Value">What the call returned, when it completed.</param>
public readonly record struct ProbeResult<T>(bool Completed, T? Value);
