using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Services.Storage;

namespace Jellyfin.Plugin.Necto.Tests.Fakes.Storage;

/// <summary>
/// Stands in for the OS's SMART reader, giving the reading a test sets for each mount point.
/// </summary>
public sealed class FakeSmartReader : ISmartReader
{
    /// <summary>Gets the readings by mount point.</summary>
    public Dictionary<string, SmartReading> Readings { get; } = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public SmartReading? Read(string mountPoint) => Readings.TryGetValue(mountPoint, out var reading) ? reading : null;
}
