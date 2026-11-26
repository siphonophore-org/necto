using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Security;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Microsoft.Extensions.Logging;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Proxy;

/// <summary>
/// Holds one certificate per route and picks the one for each TLS handshake by the name the client asked for. An
/// exact name beats a wildcard, so <c>media.example.com</c> wins over <c>*.example.com</c>.
/// </summary>
public sealed class CertificateSet : IDisposable
{
    private readonly ProxyPaths _paths;
    private readonly SecretProtector _secrets;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ActivityLogger? _activity;
    private readonly object _lock = new();
    private Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="CertificateSet"/> class.
    /// </summary>
    /// <param name="paths">The proxy's data paths.</param>
    /// <param name="secrets">Decrypts certificate passwords.</param>
    /// <param name="loggerFactory">Makes each route's logger.</param>
    /// <param name="activity">The activity log writer.</param>
    public CertificateSet(ProxyPaths paths, SecretProtector secrets, ILoggerFactory loggerFactory, ActivityLogger? activity = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _paths = paths;
        _secrets = secrets;
        _loggerFactory = loggerFactory;
        _activity = activity;
    }

    /// <summary>Raised when any route's certificate becomes available or stops being available.</summary>
    public event EventHandler? AvailabilityChanged;

    /// <summary>Gets a value indicating whether any route has a certificate, which decides whether the HTTPS listener runs.</summary>
    public bool HasCertificate
    {
        get
        {
            lock (_lock)
            {
                return _entries.Values.Any(e => e.Store.HasCertificate);
            }
        }
    }

    /// <summary>Gets the self-signed root, once a route uses a self-signed certificate.</summary>
    public RootCertificate? Root
    {
        get
        {
            lock (_lock)
            {
                return _entries.Values.Select(e => e.Store.Root).FirstOrDefault(r => r is not null);
            }
        }
    }

    /// <summary>
    /// Brings the set in line with the routes: a store per enabled route with a certificate source, and none for a
    /// removed one.
    /// </summary>
    /// <param name="routes">The proxy's routes.</param>
    /// <param name="hostsOf">The names a route's certificate covers, such as only the names Let's Encrypt can check.</param>
    public void Configure(IEnumerable<ProxyRoute> routes, Func<ProxyRoute, IReadOnlyList<string>> hostsOf)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(hostsOf);
        var wanted = routes.Where(r => r is { Enabled: true } && r.Certificate.Mode != CertificateMode.None && HostnameMatcher.Normalize(r.Host).Length > 0).ToList();
        var rootNames = wanted.Where(r => r.Certificate.Mode == CertificateMode.SelfSigned)
            .Select(r => HostnameMatcher.Normalize(r.Host))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        List<CertificateStore> removed;
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            var next = new Dictionary<string, Entry>(StringComparer.Ordinal);
            foreach (var route in wanted)
            {
                if (!_entries.TryGetValue(route.Id, out var entry))
                {
                    var store = new CertificateStore(_paths.ForRoute(route.Id), _paths, _secrets, _loggerFactory.CreateLogger<CertificateStore>(), _activity);
                    store.AvailabilityChanged += OnStoreChanged;
                    entry = new Entry(store);
                }

                entry.Host = HostnameMatcher.Normalize(route.Host);
                entry.IsJellyfin = route.IsJellyfin;
                entry.Store.Configure(route.Certificate, hostsOf(route), rootNames);
                next[route.Id] = entry;
            }

            removed = _entries.Where(e => !next.ContainsKey(e.Key)).Select(e => e.Value.Store).ToList();
            _entries = next;
        }

        foreach (var store in removed)
        {
            store.AvailabilityChanged -= OnStoreChanged;
            store.Dispose();
        }
    }

    /// <summary>
    /// Gives one route's store.
    /// </summary>
    /// <param name="routeId">The route.</param>
    /// <returns>The store, or <c>null</c> when the route has no certificate source.</returns>
    public CertificateStore? For(string routeId)
    {
        lock (_lock)
        {
            return _entries.TryGetValue(routeId, out var entry) ? entry.Store : null;
        }
    }

    /// <summary>
    /// Describes one route's certificate.
    /// </summary>
    /// <param name="routeId">The route.</param>
    /// <returns>The status, which is empty when the route has no certificate source.</returns>
    public CertificateStatus StatusOf(string routeId) => For(routeId)?.Status ?? new CertificateStatus { Mode = CertificateMode.None };

    /// <summary>
    /// Picks the certificate for a TLS handshake. A route whose name matches the one asked for is first. A local
    /// client that names no route, such as one using this server's address, gets the first Jellyfin route's
    /// certificate, then any certificate loaded.
    /// </summary>
    /// <param name="serverName">The SNI name, if any.</param>
    /// <param name="local">Whether the client is on the local network or a trusted network.</param>
    /// <returns>The certificate, or <c>null</c> to refuse the handshake.</returns>
    public SslStreamCertificateContext? Select(string? serverName, bool local)
    {
        List<Entry> entries;
        lock (_lock)
        {
            entries = _entries.Values.ToList();
        }

        var host = HostnameMatcher.Normalize(serverName);
        var match = entries.Where(e => host.Length > 0 && HostnameMatcher.Matches(host, e.Host))
            .OrderBy(e => e.Host.StartsWith('*') ? 1 : 0)
            .Select(e => e.Store.Select(serverName, local))
            .FirstOrDefault(c => c is not null);
        if (match is not null || !local)
        {
            return match;
        }

        return entries.OrderByDescending(e => e.IsJellyfin)
            .Select(e => e.Store.Select(serverName, true))
            .FirstOrDefault(c => c is not null);
    }

    /// <summary>
    /// Reloads every route's certificate from its files.
    /// </summary>
    public void Refresh()
    {
        List<CertificateStore> stores;
        lock (_lock)
        {
            stores = _entries.Values.Select(e => e.Store).ToList();
        }

        foreach (var store in stores)
        {
            store.Refresh();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        List<CertificateStore> stores;
        lock (_lock)
        {
            _disposed = true;
            stores = _entries.Values.Select(e => e.Store).ToList();
            _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        }

        foreach (var store in stores)
        {
            store.AvailabilityChanged -= OnStoreChanged;
            store.Dispose();
        }
    }

    private void OnStoreChanged(object? sender, EventArgs e) => AvailabilityChanged?.Invoke(this, EventArgs.Empty);

    private sealed class Entry
    {
        public Entry(CertificateStore store) => Store = store;

        public CertificateStore Store { get; }

        public string Host { get; set; } = string.Empty;

        public bool IsJellyfin { get; set; }
    }
}
