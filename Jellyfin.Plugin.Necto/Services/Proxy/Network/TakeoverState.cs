using System;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Hosting;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;

namespace Jellyfin.Plugin.Necto.Services.Proxy.Network;

/// <summary>
/// The persisted state of the port takeover. Lives in a file beside the configuration so it survives the
/// restart that applies it.
/// </summary>
public sealed class TakeoverState
{
    /// <summary>Gets or sets the phase.</summary>
    public TakeoverPhase Phase { get; set; } = TakeoverPhase.Unmanaged;

    /// <summary>Gets or sets Jellyfin's settings from before the takeover. Null when unmanaged.</summary>
    public NetworkSnapshot? Snapshot { get; set; }

    /// <summary>Gets or sets when the takeover was applied (UTC).</summary>
    public DateTime? AppliedUtc { get; set; }

    /// <summary>Gets or sets the last problem: a failed startup check or why settings were put back; empty when none.</summary>
    public string LastError { get; set; } = string.Empty;

    /// <summary>Gets or sets when the last problem happened (UTC).</summary>
    public DateTime? LastErrorUtc { get; set; }
}
