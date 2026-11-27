using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.WireGuard;
using Jellyfin.Plugin.Necto.Services.Configuration;
using Jellyfin.Plugin.Necto.Services.Groups;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.WireGuard;

/// <summary>
/// Starts a wgsidecar for every tunnel the settings describe. A change to a tunnel's peers or limits goes to the
/// running sidecar live, and any other change restarts only that tunnel. Peers and connections over their data limit
/// are left out until that limit's window ends, and so are peers their owners' groups do not allow.
/// </summary>
[PluginService(ServiceLifetime.Singleton)]
public sealed class TunnelSupervisor : IDisposable
{
    private readonly IServerConfigurationManager _serverConfig;
    private readonly TunnelActivity _activity;
    private readonly UsageTracker _usage;
    private readonly SidecarStore _sidecar;
    private readonly SecretProtector _secrets;
    private readonly IConfigurationStore _store;
    private readonly PermissionService _permissions;
    private readonly GroupStore _groups;
    private readonly ILogger<TunnelSupervisor> _logger;
    private readonly SemaphoreSlim _applyGate = new(1, 1);
    private readonly Dictionary<string, SidecarProcess> _running = new(StringComparer.Ordinal);
    private HashSet<Guid> _blocked = new();
    private Dictionary<Guid, PeerRefusal> _refused = new();
    private bool _seeded;
    private bool _stopped;
    private bool _warnedNoSidecar;
    private Timer? _limitCheck;

    /// <summary>
    /// Initializes a new instance of the <see cref="TunnelSupervisor"/> class.
    /// </summary>
    /// <param name="serverConfig">Gives Jellyfin's local HTTP port.</param>
    /// <param name="activity">Writes and sends tunnel events.</param>
    /// <param name="usage">Counts traffic.</param>
    /// <param name="sidecar">Finds the downloaded sidecar.</param>
    /// <param name="secrets">Decrypts and encrypts the stored keys.</param>
    /// <param name="store">Reads and changes the settings.</param>
    /// <param name="permissions">Gives what each peer's owner may have.</param>
    /// <param name="groups">Tells when a group changed, so the peers it allows apply at once.</param>
    /// <param name="logger">Logs the tunnels.</param>
    public TunnelSupervisor(
        IServerConfigurationManager serverConfig,
        TunnelActivity activity,
        UsageTracker usage,
        SidecarStore sidecar,
        SecretProtector secrets,
        IConfigurationStore store,
        PermissionService permissions,
        GroupStore groups,
        ILogger<TunnelSupervisor> logger)
    {
        _serverConfig = serverConfig ?? throw new ArgumentNullException(nameof(serverConfig));
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
        _usage = usage ?? throw new ArgumentNullException(nameof(usage));
        _sidecar = sidecar ?? throw new ArgumentNullException(nameof(sidecar));
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _groups = groups ?? throw new ArgumentNullException(nameof(groups));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Starts the tunnels, and follows every later change to the settings, the groups, and Jellyfin's network settings.
    /// </summary>
    public void Start()
    {
        if (Plugin.Instance is { } plugin)
        {
            plugin.ConfigurationChanged += OnConfigurationChanged;
        }

        // A group save does not raise ConfigurationChanged, so it is followed on its own.
        _groups.Changed += OnGroupsChanged;

        // Jellyfin's own port is the host tunnel's target, so a network change applies again.
        _serverConfig.NamedConfigurationUpdated += OnNamedConfigurationUpdated;

        // Limits are checked each minute as well as with every stats report, so a connection cut off for its limit
        // comes back when its window ends even while no tunnel runs to report.
        _limitCheck = new Timer(_ => CheckLimits(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        Apply();
    }

    /// <summary>
    /// Stops every tunnel and saves the usage counts.
    /// </summary>
    /// <param name="cancellationToken">Stops waiting for an apply in progress.</param>
    /// <returns>A task that completes once every sidecar stopped.</returns>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Plugin.Instance is { } plugin)
        {
            plugin.ConfigurationChanged -= OnConfigurationChanged;
        }

        _groups.Changed -= OnGroupsChanged;
        _serverConfig.NamedConfigurationUpdated -= OnNamedConfigurationUpdated;
        if (_limitCheck is not null)
        {
            await _limitCheck.DisposeAsync().ConfigureAwait(false);
        }

        await _applyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _stopped = true;
            await StopAllAsync().ConfigureAwait(false);
            _usage.Dispose();
        }
        finally
        {
            _applyGate.Release();
        }
    }

    /// <summary>
    /// Stops every tunnel and starts the ones the settings describe again.
    /// </summary>
    /// <returns>A task that completes once the tunnels started.</returns>
    public async Task RestartAsync()
    {
        await _applyGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopAllAsync().ConfigureAwait(false);
        }
        finally
        {
            _applyGate.Release();
        }

        await ApplyAsync().ConfigureAwait(false);
    }

    /// <summary>Reads every tunnel's state, with each peer's traffic and handshake.</summary>
    /// <returns>One status per running tunnel.</returns>
    public IReadOnlyList<TunnelStatus> Status()
    {
        var connections = _store.Read(c => c.WireGuard.Connections.Select(x => (Key: x.Id.ToString("N"), x.Name)).ToList());
        lock (_running)
        {
            return _running.Values.Select(p =>
            {
                var status = p.Status();
                status.Name = p.Key == SidecarConfigBuilder.HostKey
                    ? "Host"
                    : connections.FirstOrDefault(c => c.Key == p.Key).Name ?? p.Key;
                return status;
            }).ToList();
        }
    }

    /// <summary>Gets the peers and connections left out for being over their data limit.</summary>
    /// <returns>Their identifiers.</returns>
    public IReadOnlyCollection<Guid> Blocked()
    {
        lock (_running)
        {
            return _blocked.ToList();
        }
    }

    /// <summary>Gets the peers left out because of their owners' groups.</summary>
    /// <returns>Each peer with why.</returns>
    public IReadOnlyDictionary<Guid, PeerRefusal> Refused()
    {
        lock (_running)
        {
            return new Dictionary<Guid, PeerRefusal>(_refused);
        }
    }

    /// <summary>Applies the settings again in the background, such as after a group changed.</summary>
    public void Apply() => _ = Task.Run(ApplyAsync, CancellationToken.None);

    /// <inheritdoc />
    public void Dispose()
    {
        _limitCheck?.Dispose();
        _applyGate.Dispose();
    }

    private void OnConfigurationChanged(object? sender, BasePluginConfiguration e) => Apply();

    private void OnGroupsChanged(object? sender, EventArgs e) => Apply();

    private void OnNamedConfigurationUpdated(object? sender, ConfigurationUpdateEventArgs e)
    {
        if (string.Equals(e.Key, "network", StringComparison.OrdinalIgnoreCase))
        {
            Apply();
        }
    }

    // Applies again when the peers over a limit or refused by groups are not the ones left out, such as when a
    // window ended or a user became an administrator.
    private void CheckLimits()
    {
        var config = _store.Read(c => ConfigurationCopy.Of(c.WireGuard));
        var over = _usage.OverLimit(config, OwnerLimit);
        var refused = PeerAccess.Refused(config, _permissions.For);
        bool changed;
        lock (_running)
        {
            changed = !over.SetEquals(_blocked) || !PeerAccess.SameRefusals(refused, _refused);
        }

        if (changed)
        {
            Apply();
        }
    }

    private (long Limit, LimitPeriod Period) OwnerLimit(Guid userId) => PeerAccess.OwnerLimit(_permissions.For(userId));

    private async Task StopAllAsync()
    {
        List<SidecarProcess> all;
        lock (_running)
        {
            all = _running.Values.ToList();
            _running.Clear();
        }

        await Task.WhenAll(all.Select(StopProcessAsync)).ConfigureAwait(false);
    }

    // Stops one sidecar. A failure is logged and does not keep the others from stopping.
    private async Task StopProcessAsync(SidecarProcess process)
    {
        try
        {
            await process.DisposeAsync().ConfigureAwait(false);
            _logger.LogDebug("Stopped WireGuard tunnel {Key}", process.Key);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            _logger.LogWarning(ex, "WireGuard tunnel {Key} did not stop cleanly", process.Key);
        }
        finally
        {
            _usage.Forget(process.Key);
        }
    }

    // Counts a tunnel's traffic, and applies again when a limit is crossed or a limit's window ends.
    private void OnStats(string tunnelKey, List<PeerStatus> peers)
    {
        var config = _store.Read(c => ConfigurationCopy.Of(c.WireGuard));
        _usage.Record(tunnelKey, peers, key => IdOf(config, tunnelKey, key));
        CheckLimits();
    }

    private static Guid? IdOf(WireGuardSettings config, string tunnelKey, string publicKey)
    {
        if (tunnelKey == SidecarConfigBuilder.HostKey)
        {
            return config.Peers.FirstOrDefault(p => p.PublicKey == publicKey)?.Id;
        }

        return Guid.TryParseExact(tunnelKey, "N", out var id) ? id : null;
    }

    // Runs the apply in the background. A failure is logged here, the last place it can be seen.
    private async Task ApplyAsync()
    {
        try
        {
            await ApplyCoreAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "WireGuard tunnels could not be applied");
        }
    }

    // Gives every peer a preshared key while preshared keys are on, such as after they were turned on.
    private bool EnsurePresharedKeys(WireGuardSettings config)
    {
        if (!config.UsePresharedKeys || config.Peers.All(p => TunnelKeys.IsValid(_secrets.Unprotect(p.PresharedKey)) || SecretProtector.IsProtected(p.PresharedKey)))
        {
            return false;
        }

        _store.Mutate(c =>
        {
            foreach (var peer in c.WireGuard.Peers.Where(p => !SecretProtector.IsProtected(p.PresharedKey) && !TunnelKeys.IsValid(p.PresharedKey)))
            {
                peer.PresharedKey = _secrets.Protect(TunnelKeys.NewPresharedKey());
            }

            return true;
        });
        return true;
    }

    // Makes the host's key pair once WireGuard can run and the host has none.
    private bool EnsureHostKey(WireGuardSettings config, string sidecar)
    {
        if (!config.Enabled || !config.HostEnabled || TunnelKeys.IsValid(_secrets.Unprotect(config.HostPrivateKey)))
        {
            return false;
        }

        // A stored key that cannot be decrypted is kept, so a lost key folder never replaces the host's identity.
        if (SecretProtector.IsProtected(config.HostPrivateKey))
        {
            _logger.LogError("The WireGuard host key cannot be decrypted, so the host does not start");
            return false;
        }

        var privateKey = TunnelKeys.NewPrivateKey();
        var publicKey = TunnelKeys.PublicKey(sidecar, privateKey);
        _store.Mutate(c =>
        {
            c.WireGuard.HostPrivateKey = _secrets.Protect(privateKey);
            c.WireGuard.HostPublicKey = publicKey;
            return true;
        });
        _logger.LogInformation("Made the WireGuard host key pair");
        return true;
    }

    private async Task ApplyCoreAsync()
    {
        await _applyGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_stopped)
            {
                return;
            }

            var config = _store.Read(c => ConfigurationCopy.Of(c.WireGuard));
            var path = config.Enabled ? _sidecar.Find() : null;
            // A key made here is saved without a configuration change event, so the settings are read again.
            if (EnsurePresharedKeys(config) | (path is not null && EnsureHostKey(config, path)))
            {
                config = _store.Read(c => ConfigurationCopy.Of(c.WireGuard));
            }

            var over = _usage.OverLimit(config, OwnerLimit);
            var refused = PeerAccess.Refused(config, _permissions.For);
            HashSet<Guid> before;
            lock (_running)
            {
                before = _blocked;
                _blocked = over;
                _refused = refused;
            }

            // The first pass after a start only learns which limits were already reached, so a restart does not log them again.
            if (_seeded)
            {
                foreach (var id in over.Except(before))
                {
                    _activity.DataLimitReached(id);
                }
            }

            _seeded = true;

            var desired = new Dictionary<string, SidecarConfig>(StringComparer.Ordinal);
            if (path is null)
            {
                // Logged once, since every apply would say the same.
                if (config.Enabled && !_warnedNoSidecar)
                {
                    _warnedNoSidecar = true;
                    _logger.LogError("WireGuard is on but its sidecar is not downloaded, so no tunnel can run");
                }
            }
            else
            {
                _warnedNoSidecar = false;
                var port = _serverConfig.GetNetworkConfiguration().InternalHttpPort;
                var left = new HashSet<Guid>(over);
                left.UnionWith(refused.Keys);
                desired = SidecarConfigBuilder.Build(config, _secrets.Unprotect, port, left);
            }

            var stale = new List<SidecarProcess>();
            var updates = new List<(SidecarProcess Process, SidecarConfig Config)>();
            lock (_running)
            {
                foreach (var p in _running.Values)
                {
                    if (!desired.TryGetValue(p.Key, out var next)
                        || JsonSerializer.Serialize(SidecarConfigBuilder.WithoutPeers(next), SidecarProcess.ConfigJson) != p.StaticLine)
                    {
                        stale.Add(p);
                    }
                    else if (JsonSerializer.Serialize(next, SidecarProcess.ConfigJson) != p.ConfigLine)
                    {
                        updates.Add((p, next));
                    }
                }

                foreach (var p in stale)
                {
                    _running.Remove(p.Key);
                }
            }

            foreach (var p in stale)
            {
                await StopProcessAsync(p).ConfigureAwait(false);
            }

            foreach (var (process, next) in updates)
            {
                await process.UpdateAsync(next).ConfigureAwait(false);
                _logger.LogDebug("Updated the peers of WireGuard tunnel {Key}", process.Key);
            }

            foreach (var (key, next) in desired)
            {
                lock (_running)
                {
                    if (_running.ContainsKey(key))
                    {
                        continue;
                    }

                    var process = new SidecarProcess(key, path!, next, _activity, OnStats, _logger);
                    _running[key] = process;
                    process.Start();
                }

                _logger.LogDebug("Started WireGuard tunnel {Key}", key);
            }
        }
        finally
        {
            _applyGate.Release();
        }
    }
}
