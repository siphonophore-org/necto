using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Hosting;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Services.Proxy.Reachability;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
using Jellyfin.Plugin.Necto.Api.Ddns;
using Jellyfin.Plugin.Necto.Api.Proxy;

namespace Jellyfin.Plugin.Necto.Services.Proxy.Acme;

/// <summary>
/// A DNS-01 TXT record this plugin published and has not yet removed, kept on disk so one left behind by a
/// failed removal, a crash or a restart is removed on a later run.
/// </summary>
public sealed class PendingTxt
{
    /// <summary>Gets or sets the id of the DDNS record whose credentials published it.</summary>
    public string RecordId { get; set; } = string.Empty;

    /// <summary>Gets or sets the full TXT name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the TXT value.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Gets or sets when it was published (UTC).</summary>
    public DateTime PublishedUtc { get; set; }
}

/// <summary>
/// The TXT records still to remove.
/// </summary>
public sealed class PendingTxtList
{
    /// <summary>Gets or sets the records.</summary>
    public List<PendingTxt> Records { get; set; } = new();

    /// <summary>
    /// Drops the entry for a TXT record. Matched by what it holds, since the list is read back from its file
    /// and never holds the caller's own copy.
    /// </summary>
    /// <param name="item">The record.</param>
    /// <returns>How many entries were dropped.</returns>
    public int Forget(PendingTxt item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return Records.RemoveAll(p => p.Name == item.Name && p.Value == item.Value);
    }
}
