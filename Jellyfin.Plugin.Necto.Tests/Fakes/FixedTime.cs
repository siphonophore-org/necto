using System;

namespace Jellyfin.Plugin.Necto.Tests.Fakes;

/// <summary>
/// A clock that always reads the time it was given, so a test decides what now is.
/// </summary>
public sealed class FixedTime : TimeProvider
{
    private readonly DateTimeOffset _now;

    public FixedTime(DateTimeOffset now) => _now = now;

    public override DateTimeOffset GetUtcNow() => _now;
}
