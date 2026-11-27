using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.Ddns;

namespace Jellyfin.Plugin.Necto.Api.Ddns;

/// <summary>
/// One row of the DDNS tool's table: a record with its status, the hosts it covers when it follows the proxy, and the
/// state the table's filter goes by.
/// </summary>
/// <param name="Record">The record, with its credentials replaced by the kept marker and its status filled in.</param>
/// <param name="Followed">Each host a record that follows the proxy covers, with its status.</param>
/// <param name="State">Off, NotChecked, UpToDate, or Attention, from <see cref="RecordRow.StateOf"/>.</param>
public sealed record RecordRow(DNSRecord Record, IReadOnlyList<DNSRecord> Followed, string State)
{
    /// <summary>
    /// The state of a record that is off.
    /// </summary>
    public const string Off = "Off";

    /// <summary>
    /// The state of a record that has not run yet.
    /// </summary>
    public const string NotChecked = "NotChecked";

    /// <summary>
    /// The state of a record whose last run updated it or found its address unchanged.
    /// </summary>
    public const string UpToDate = "UpToDate";

    /// <summary>
    /// The state of a record whose last run failed, found no address, or is paused.
    /// </summary>
    public const string Attention = "Attention";

    private static readonly string[] Current = { "Updated", "IP Unchanged" };

    // A failure matters most, then a pause, then a missing address.
    private static readonly string[] Problems = { "Failed", "Backing off", "No address" };

    /// <summary>
    /// Gives the state that stands for a record: Off while it is off, else its own last run, or for a record that
    /// follows the proxy the last run of the host that matters most.
    /// </summary>
    /// <param name="record">The record with its status.</param>
    /// <param name="followed">The hosts it covers, with their status.</param>
    /// <returns>Off, NotChecked, UpToDate, or Attention.</returns>
    public static string StateOf(DNSRecord record, IReadOnlyList<DNSRecord> followed)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!record.Enabled)
        {
            return Off;
        }

        var actions = record.FollowProxyHosts && followed is { Count: > 0 }
            ? followed.Select(f => f.LastAction).ToList()
            : new List<string> { record.LastAction };
        if (actions.Any(a => Problems.Contains(a, StringComparer.Ordinal)))
        {
            return Attention;
        }

        return actions.Count > 0 && actions.All(a => Current.Contains(a, StringComparer.Ordinal)) ? UpToDate : NotChecked;
    }
}
