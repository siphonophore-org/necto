using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.WireGuard;
using Siphonophore.Stolon;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Necto.Services.WireGuard;

/// <summary>
/// Counts each peer's and connection's traffic in the current hour, day, week, month, and year, and finds
/// the ones over their data limit. Each count starts over when its window does, in UTC, so a peer cut off
/// for a limit comes back when that limit's window ends. Counts are kept under Jellyfin's data folder.
/// </summary>
[PluginService(ServiceLifetime.Singleton)]
public sealed class UsageTracker : IDisposable
{
    private static readonly TimeSpan SaveEvery = TimeSpan.FromMinutes(1);
    private static readonly LimitPeriod[] Periods = Enum.GetValues<LimitPeriod>();

    private readonly JsonFileStore<UsageData>? _store;
    private readonly Func<DateTime> _now;
    private readonly object _gate = new();
    private readonly UsageData _data;

    // The last counter the sidecar reported for each tunnel and peer key. A smaller counter means the
    // sidecar restarted, so its whole counter is new traffic.
    private readonly Dictionary<string, long> _last = new(StringComparer.Ordinal);
    private DateTime _savedUtc;
    private bool _dirty;

    /// <summary>
    /// Initializes a new instance of the <see cref="UsageTracker"/> class, kept in Jellyfin's data folder.
    /// </summary>
    /// <param name="paths">The application paths.</param>
    /// <param name="logger">The logger.</param>
    public UsageTracker(IApplicationPaths paths, ILogger<UsageTracker> logger)
        : this(new JsonFileStore<UsageData>(Path.Combine(paths.DataPath, "necto", "wireguard", "usage.json"), logger), () => DateTime.UtcNow)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UsageTracker"/> class with a given store and clock, for tests.
    /// </summary>
    /// <param name="store">The file store, or null to keep counts in memory only.</param>
    /// <param name="now">The clock.</param>
    public UsageTracker(JsonFileStore<UsageData>? store, Func<DateTime> now)
    {
        _store = store;
        _now = now;
        _data = store?.Load() ?? new UsageData();
        _savedUtc = now();
    }

    /// <summary>
    /// Names the window a moment falls in, such as 2026-11-24T01 for an hour or 2026-W48 for a week.
    /// </summary>
    /// <param name="period">The period.</param>
    /// <param name="utc">The moment, in UTC.</param>
    /// <returns>The window's name.</returns>
    public static string WindowOf(LimitPeriod period, DateTime utc) => period switch
    {
        LimitPeriod.Hour => utc.ToString("yyyy-MM-dd'T'HH", CultureInfo.InvariantCulture),
        LimitPeriod.Day => utc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        LimitPeriod.Week => string.Create(CultureInfo.InvariantCulture, $"{ISOWeek.GetYear(utc)}-W{ISOWeek.GetWeekOfYear(utc):00}"),
        LimitPeriod.Month => utc.ToString("yyyy-MM", CultureInfo.InvariantCulture),
        _ => utc.ToString("yyyy", CultureInfo.InvariantCulture)
    };

    /// <summary>
    /// Adds a tunnel's latest counters. Each peer's traffic is the growth of its received and sent
    /// bytes since the last report.
    /// </summary>
    /// <param name="tunnelKey">The tunnel the counters came from.</param>
    /// <param name="peers">Each peer's counters.</param>
    /// <param name="idOf">Finds the peer or connection a public key belongs to.</param>
    public void Record(string tunnelKey, IEnumerable<PeerStatus> peers, Func<string, Guid?> idOf)
    {
        ArgumentNullException.ThrowIfNull(peers);
        ArgumentNullException.ThrowIfNull(idOf);

        var now = _now();
        lock (_gate)
        {
            foreach (var peer in peers)
            {
                var counter = peer.RxBytes + peer.TxBytes;
                var key = tunnelKey + "/" + peer.PublicKey;
                var last = _last.GetValueOrDefault(key);
                var delta = counter >= last ? counter - last : counter;
                _last[key] = counter;
                if (delta <= 0 || idOf(peer.PublicKey) is not { } id)
                {
                    continue;
                }

                var name = id.ToString("N");
                if (!_data.Counts.TryGetValue(name, out var counts))
                {
                    counts = new Dictionary<string, PeriodCount>(StringComparer.Ordinal);
                    _data.Counts[name] = counts;
                }

                foreach (var period in Periods)
                {
                    var window = WindowOf(period, now);
                    var label = period.ToString();
                    if (!counts.TryGetValue(label, out var count) || count.Window != window)
                    {
                        count = new PeriodCount { Window = window };
                        counts[label] = count;
                    }

                    count.Bytes += delta;
                }

                _dirty = true;
            }

            if (_dirty && now - _savedUtc >= SaveEvery)
            {
                SaveLocked();
            }
        }
    }

    /// <summary>Forgets a tunnel's last counters, when its sidecar stops.</summary>
    /// <param name="tunnelKey">The tunnel.</param>
    public void Forget(string tunnelKey)
    {
        lock (_gate)
        {
            foreach (var key in _last.Keys.Where(k => k.StartsWith(tunnelKey + "/", StringComparison.Ordinal)).ToList())
            {
                _last.Remove(key);
            }
        }
    }

    /// <summary>Reads a peer's or connection's bytes in the current window of a period.</summary>
    /// <param name="id">The peer or connection.</param>
    /// <param name="period">The period.</param>
    /// <returns>The bytes sent and received together, or 0 once the window has passed.</returns>
    public long BytesOf(Guid id, LimitPeriod period)
    {
        var window = WindowOf(period, _now());
        lock (_gate)
        {
            return _data.Counts.TryGetValue(id.ToString("N"), out var counts)
                && counts.TryGetValue(period.ToString(), out var count) && count.Window == window
                ? count.Bytes
                : 0;
        }
    }

    /// <summary>Reads every peer's and connection's bytes in the current window of each period.</summary>
    /// <returns>The bytes by identifier, then by period.</returns>
    public Dictionary<Guid, Dictionary<LimitPeriod, long>> All()
    {
        List<Guid> ids;
        lock (_gate)
        {
            // A damaged entry is skipped rather than failing every read.
            ids = _data.Counts.Keys.Select(k => Guid.TryParse(k, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).ToList();
        }

        return ids.ToDictionary(id => id, id => Periods.ToDictionary(p => p, p => BytesOf(id, p)));
    }

    /// <summary>
    /// Finds every peer and connection over a data limit: its own, the default, or its owner's for all
    /// their peers together, which the owner's groups give. Each limit counts over its own period.
    /// </summary>
    /// <param name="config">The WireGuard settings.</param>
    /// <param name="ownerLimit">Gives the data limit and period of all of a user's peers together, where 0 is none.</param>
    /// <returns>The identifiers over their limit.</returns>
    public HashSet<Guid> OverLimit(WireGuardSettings config, Func<Guid, (long Limit, LimitPeriod Period)>? ownerLimit = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        var over = new HashSet<Guid>();

        foreach (var peer in config.Peers)
        {
            var (limit, period) = peer.DataLimit is { } own ? (own, peer.DataPeriod) : (config.PeerDataLimit, config.PeerDataPeriod);
            if (limit > 0 && BytesOf(peer.Id, period) >= limit)
            {
                over.Add(peer.Id);
            }
        }

        if (ownerLimit is not null)
        {
            // A peer with no owner counts only against its own limit, not as one shared user.
            foreach (var owner in config.Peers.Where(p => p.UserId != Guid.Empty).GroupBy(p => p.UserId))
            {
                var (limit, period) = ownerLimit(owner.Key);
                if (limit > 0 && owner.Sum(p => BytesOf(p.Id, period)) >= limit)
                {
                    over.UnionWith(owner.Select(p => p.Id));
                }
            }
        }

        foreach (var connection in config.Connections)
        {
            var (limit, period) = connection.DataLimit is { } own
                ? (own, connection.DataPeriod)
                : (config.ConnectionDataLimit, config.ConnectionDataPeriod);
            if (limit > 0 && BytesOf(connection.Id, period) >= limit)
            {
                over.Add(connection.Id);
            }
        }

        return over;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_dirty)
            {
                SaveLocked();
            }
        }
    }

    private void SaveLocked()
    {
        _store?.Save(_data);
        _savedUtc = _now();
        _dirty = false;
    }
}
