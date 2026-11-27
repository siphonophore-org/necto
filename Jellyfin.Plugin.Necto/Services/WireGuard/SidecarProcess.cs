using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Configuration.WireGuard;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Necto.Services.WireGuard;

/// <summary>
/// Runs one wgsidecar for one tunnel, restarting it with a growing delay when it exits.
/// </summary>
public sealed class SidecarProcess : IAsyncDisposable
{
    /// <summary>The JSON options the sidecar config is written with.</summary>
    public static readonly JsonSerializerOptions ConfigJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions EventJson = new() { PropertyNameCaseInsensitive = true };

    // Delays before each restart. A tunnel that fails this many times in a row is reported once.
    private static readonly TimeSpan[] RestartDelays =
    {
        TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60)
    };

    private readonly string _path;
    private readonly TunnelActivity _activity;
    private readonly Action<string, List<PeerStatus>> _onStats;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private readonly SemaphoreSlim _write = new(1, 1);
    private Task _loop = Task.CompletedTask;
    private List<PeerStatus> _peers = new();
    private string _state = "Starting";
    private string? _error;
    private string _configLine;
    private StreamWriter? _stdin;

    /// <summary>
    /// Initializes a new instance of the <see cref="SidecarProcess"/> class.
    /// </summary>
    /// <param name="key">The tunnel's key.</param>
    /// <param name="path">The sidecar binary.</param>
    /// <param name="config">The tunnel's config.</param>
    /// <param name="activity">The activity log writer.</param>
    /// <param name="onStats">Takes each stats report, for usage counting.</param>
    /// <param name="logger">The logger.</param>
    public SidecarProcess(string key, string path, SidecarConfig config, TunnelActivity activity, Action<string, List<PeerStatus>> onStats, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(config);
        Key = key;
        _path = path;
        _configLine = JsonSerializer.Serialize(config, ConfigJson);
        StaticLine = JsonSerializer.Serialize(SidecarConfigBuilder.WithoutPeers(config), ConfigJson);
        _activity = activity;
        _onStats = onStats;
        _logger = logger;
    }

    /// <summary>Gets the tunnel's key.</summary>
    public string Key { get; }

    /// <summary>Gets the config without its peers, to tell whether a change needs a restart.</summary>
    public string StaticLine { get; }

    /// <summary>Gets the config the process runs with.</summary>
    public string ConfigLine
    {
        get
        {
            lock (_gate)
            {
                return _configLine;
            }
        }
    }

    /// <summary>
    /// Replaces the peers and their limits without a restart. Peers that stay keep their sessions. A
    /// restart later starts from the new config too.
    /// </summary>
    /// <param name="config">The new config, whose settings other than peers match the running one.</param>
    /// <returns>A task that completes when the line is written.</returns>
    public async Task UpdateAsync(SidecarConfig config)
    {
        lock (_gate)
        {
            _configLine = JsonSerializer.Serialize(config, ConfigJson);
        }

        await _write.WaitAsync().ConfigureAwait(false);
        try
        {
            // Whoever holds the write lock writes the config current at that moment, so a newer config is
            // never overtaken by an older one.
            StreamWriter? stdin;
            string line;
            lock (_gate)
            {
                stdin = _stdin;
                line = _configLine;
            }

            if (stdin is null)
            {
                return;
            }

            await stdin.WriteLineAsync(line).ConfigureAwait(false);
            await stdin.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The sidecar exited meanwhile. Its restart reads the new config.
            _logger.LogDebug(ex, "WireGuard tunnel {Key} exited before its peers were updated", Key);
        }
        finally
        {
            _write.Release();
        }
    }

    /// <summary>Starts the run loop.</summary>
    public void Start() => _loop = Task.Run(() => RunLoopAsync(_stop.Token));

    /// <summary>Reads the tunnel's current state.</summary>
    /// <returns>The state, without the name.</returns>
    public TunnelStatus Status()
    {
        lock (_gate)
        {
            return new TunnelStatus { Key = Key, State = _state, Error = _error, Peers = _peers.ToList() };
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Stopping is the expected way out of the loop.
        }

        _stop.Dispose();
        _write.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken stop)
    {
        var failures = 0;
        while (!stop.IsCancellationRequested)
        {
            var ranFor = Stopwatch.StartNew();
            int exitCode;
            try
            {
                exitCode = await RunOnceAsync(stop).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                SetError(ex.Message);
                exitCode = -1;
            }

            // A tunnel that ran a while before exiting starts its count over.
            failures = ranFor.Elapsed > TimeSpan.FromMinutes(5) ? 1 : failures + 1;
            SetState("Failed");
            _logger.LogDebug("WireGuard tunnel {Key} exited with code {Code}, attempt {Attempt}", Key, exitCode, failures);
            if (failures == RestartDelays.Length)
            {
                _logger.LogError("WireGuard tunnel {Key} keeps failing: {Error}", Key, _error);
                _activity.TunnelFailed(Key, _error);
            }

            await Task.Delay(RestartDelays[Math.Min(failures, RestartDelays.Length) - 1], stop).ConfigureAwait(false);
        }
    }

    private async Task<int> RunOnceAsync(CancellationToken stop)
    {
        SetState("Starting");
        var start = new ProcessStartInfo(_path, "run")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = start };
        process.Start();
        var up = new HashSet<string>(StringComparer.Ordinal);
        // Both readers are awaited in the finally below, before the process is disposed.
#pragma warning disable CA2025
        var stdout = ReadEventsAsync(process, up);
        var stderr = ReadLogAsync(process);
#pragma warning restore CA2025

        try
        {
            // The config goes on stdin, so the private key never touches disk. Stdin stays open: closing it
            // stops the sidecar, so it never outlives Jellyfin. The first line is written under the write
            // lock, and whatever config is current when the lock is held is the one written, so an update
            // that came in meanwhile is never overtaken by an older line.
            await _write.WaitAsync(stop).ConfigureAwait(false);
            try
            {
                string line;
                lock (_gate)
                {
                    line = _configLine;
                }

                await process.StandardInput.WriteLineAsync(line).ConfigureAwait(false);
                await process.StandardInput.FlushAsync(stop).ConfigureAwait(false);

                // An update that changed the config while the first line was written is sent now.
                string current;
                lock (_gate)
                {
                    _stdin = process.StandardInput;
                    current = _configLine;
                }

                if (current != line)
                {
                    await process.StandardInput.WriteLineAsync(current).ConfigureAwait(false);
                    await process.StandardInput.FlushAsync(stop).ConfigureAwait(false);
                }
            }
            finally
            {
                _write.Release();
            }

            await process.WaitForExitAsync(stop).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            lock (_gate)
            {
                _stdin = null;
            }

            process.StandardInput.Close();
            using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try
            {
                await process.WaitForExitAsync(grace.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                process.Kill(true);
            }

            throw;
        }
        catch (IOException)
        {
            // The sidecar exited before it read its config, such as on a bad address. Its own last line,
            // already kept as the error, says why, and the loop starts it again.
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                _stdin = null;
            }

            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);

            // Peers of a tunnel that stopped are no longer connected.
            foreach (var key in up)
            {
                Safely("peer-down", () => _activity.PeerDown(Key, key));
            }

            lock (_gate)
            {
                _peers = new List<PeerStatus>();
            }
        }

        return process.ExitCode;
    }

    private async Task ReadEventsAsync(Process process, HashSet<string> up)
    {
        string? line;
        while ((line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false)) is not null)
        {
            SidecarEvent? e;
            try
            {
                e = JsonSerializer.Deserialize<SidecarEvent>(line, EventJson);
            }
            catch (JsonException)
            {
                _logger.LogDebug("WireGuard tunnel {Key} wrote a line that is not an event", Key);
                continue;
            }

            switch (e?.Event)
            {
                case "ready":
                    SetState("Running");
                    SetError(null);
                    break;
                case "peer-up" when e.PublicKey is not null:
                    up.Add(e.PublicKey);
                    Safely("peer-up", () => _activity.PeerUp(Key, e.PublicKey, e.Endpoint));
                    break;
                case "peer-down" when e.PublicKey is not null:
                    up.Remove(e.PublicKey);
                    Safely("peer-down", () => _activity.PeerDown(Key, e.PublicKey));
                    break;
                case "stats":
                    var peers = (e.Peers ?? new List<SidecarPeerStats>()).Select(p => new PeerStatus
                    {
                        PublicKey = p.PublicKey ?? string.Empty,
                        Endpoint = p.Endpoint,
                        LastHandshakeUtc = p.LastHandshake > 0 ? DateTimeOffset.FromUnixTimeSeconds(p.LastHandshake).UtcDateTime : null,
                        RxBytes = p.RxBytes,
                        TxBytes = p.TxBytes,
                        Connected = p.Connected
                    }).ToList();
                    lock (_gate)
                    {
                        _peers = peers;
                    }

                    Safely("stats", () => _onStats(Key, peers));
                    break;
            }
        }
    }

    // Runs an event's handler so that a failure in it, such as the usage file failing to save, is logged
    // and never stops the reader. A stopped reader would leave the sidecar blocked on a full pipe.
    private void Safely(string eventName, Action handle)
    {
        try
        {
            handle();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "WireGuard tunnel {Key} could not handle a {Event} event", Key, eventName);
        }
    }

    private async Task ReadLogAsync(Process process)
    {
        string? line;
        while ((line = await process.StandardError.ReadLineAsync().ConfigureAwait(false)) is not null)
        {
            _logger.LogDebug("WireGuard tunnel {Key}: {Line}", Key, line);

            // The sidecar prefixes its own lines with a timestamp. The text after it is the error to show.
            var text = line.Length > 20 && char.IsDigit(line[0]) ? line[20..] : line;
            if (!text.StartsWith("up:", StringComparison.Ordinal) && !text.StartsWith("inbound:", StringComparison.Ordinal)
                && !text.StartsWith("outbound:", StringComparison.Ordinal) && text != "stopping")
            {
                SetError(text);
            }
        }
    }

    private void SetState(string state)
    {
        lock (_gate)
        {
            _state = state;
        }
    }

    private void SetError(string? error)
    {
        lock (_gate)
        {
            _error = TunnelError.Plain(error);
        }
    }

    private sealed class SidecarEvent
    {
        public string? Event { get; set; }

        public string? PublicKey { get; set; }

        public string? Endpoint { get; set; }

        public List<SidecarPeerStats>? Peers { get; set; }
    }

    private sealed class SidecarPeerStats
    {
        public string? PublicKey { get; set; }

        public string? Endpoint { get; set; }

        public long LastHandshake { get; set; }

        public long RxBytes { get; set; }

        public long TxBytes { get; set; }

        public bool Connected { get; set; }
    }
}
