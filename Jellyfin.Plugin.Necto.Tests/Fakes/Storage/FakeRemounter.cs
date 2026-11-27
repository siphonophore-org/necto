using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Services.Storage;

namespace Jellyfin.Plugin.Necto.Tests.Fakes.Storage;

/// <summary>
/// Stands in for the OS's remounter, with the sources and the answer a test sets, recording each try.
/// </summary>
public sealed class FakeRemounter : IRemounter
{
    /// <inheritdoc />
    public bool TakesAddress { get; set; }

    /// <summary>Gets the source each mount point was mounted from.</summary>
    public Dictionary<string, string> Sources { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets or sets what each try gives, from its mount point and source.</summary>
    public Func<string, string?, RemountResult> Answer { get; set; } = (_, _) => RemountResult.Failed(RemountFailure.Unreachable);

    /// <summary>Gets every try, in order.</summary>
    public List<(string MountPoint, string? Source)> Calls { get; } = new();

    /// <inheritdoc />
    public string? SourceOf(string mountPoint) => Sources.TryGetValue(mountPoint, out var source) ? source : null;

    /// <inheritdoc />
    public RemountResult Remount(string mountPoint, string? source)
    {
        Calls.Add((mountPoint, source));
        return Answer(mountPoint, source);
    }

    /// <summary>
    /// Counts the tries at one mount point from one source.
    /// </summary>
    /// <param name="mountPoint">The mount point.</param>
    /// <param name="source">The source.</param>
    /// <returns>How many tries matched.</returns>
    public int Tries(string mountPoint, string? source) => Calls.Count(c => c.MountPoint == mountPoint && c.Source == source);
}
