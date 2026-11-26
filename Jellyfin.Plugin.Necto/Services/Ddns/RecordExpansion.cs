using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy;

namespace Jellyfin.Plugin.Necto.Services.Ddns;

/// <summary>
/// Turns the records into the ones a run pushes. A record that follows the proxy's host names stands for one record
/// per enabled internet route under its zone, each named after the route's host and keeping its own status.
/// </summary>
public static class RecordExpansion
{
    /// <summary>The character between a following record's id and the host it stands for.</summary>
    public const char Separator = '~';

    /// <summary>
    /// Expands the records. A record that follows the proxy but covers no route stands for nothing.
    /// </summary>
    /// <param name="records">The records to expand.</param>
    /// <param name="proxy">The proxy's settings, whose routes a following record covers.</param>
    /// <returns>Copies of the records, following ones replaced by one per host.</returns>
    public static List<DNSRecord> Expand(IEnumerable<DNSRecord> records, ProxySettings? proxy)
    {
        ArgumentNullException.ThrowIfNull(records);
        var hosts = HostsOf(proxy);
        var result = new List<DNSRecord>();
        foreach (var record in records.Where(r => r is not null))
        {
            if (!record.FollowProxyHosts)
            {
                result.Add(record.Clone());
                continue;
            }

            foreach (var host in Covered(record, hosts))
            {
                var copy = record.Clone();
                copy.Hostname = host;
                copy.Id = record.Id + Separator + host;
                result.Add(copy);
            }
        }

        return result;
    }

    /// <summary>
    /// Gives the hosts a following record covers: every route host under its zone, or every route host when it
    /// names no zone.
    /// </summary>
    /// <param name="record">The record.</param>
    /// <param name="hosts">The proxy's internet route hosts.</param>
    /// <returns>The hosts, in route order.</returns>
    public static List<string> Covered(DNSRecord record, IEnumerable<string> hosts)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(hosts);
        var zone = (record.Zone ?? string.Empty).Trim().TrimEnd('.').ToLowerInvariant();
        var named = zone.Contains('.', StringComparison.Ordinal);
        return hosts.Where(h => !named || h == zone || h.EndsWith("." + zone, StringComparison.Ordinal)).ToList();
    }

    /// <summary>
    /// Gives the exact host names of the enabled routes the internet may reach. A wildcard cannot be pushed as a
    /// record, and a local route's name is not meant to point at the public address.
    /// </summary>
    /// <param name="proxy">The proxy's settings, or <c>null</c>.</param>
    /// <returns>The distinct hosts, in route order.</returns>
    public static List<string> HostsOf(ProxySettings? proxy)
        => (proxy?.Routes ?? new List<ProxyRoute>())
            .Where(r => r is { Enabled: true, LocalOnly: false })
            .Select(r => HostnameMatcher.Normalize(r.Host))
            .Where(h => h.Length > 0 && !h.StartsWith('*'))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Gives the id of the record a pushed record came from.
    /// </summary>
    /// <param name="id">A pushed record's id.</param>
    /// <returns>The stored record's id.</returns>
    public static string SourceId(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var at = id.IndexOf(Separator, StringComparison.Ordinal);
        return at < 0 ? id : id[..at];
    }
}
