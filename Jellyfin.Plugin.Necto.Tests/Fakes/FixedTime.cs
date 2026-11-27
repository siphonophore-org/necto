using System;

namespace Jellyfin.Plugin.Necto.Tests.Fakes;

/// <summary>
/// A clock that reads the time it was given until a test moves it, so a test decides what now is.
/// </summary>
public sealed class FixedTime : TimeProvider
{
    public FixedTime(DateTimeOffset now) => Now = now;

    /// <summary>Gets or sets the time now.</summary>
    public DateTimeOffset Now { get; set; }

    public override DateTimeOffset GetUtcNow() => Now;
}
