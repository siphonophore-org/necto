using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;

namespace Jellyfin.Plugin.Necto.Tests.Fakes;

/// <summary>
/// A DNS provider that can publish TXT records, whose answers each test scripts and whose calls it records.
/// </summary>
public sealed class FakeTxtProvider : IDNSProvider, IAcmeDnsProvider
{
    private readonly Queue<bool> _removals = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="FakeTxtProvider"/> class.
    /// </summary>
    /// <param name="kind">The provider it stands for.</param>
    /// <param name="removals">What each removal answers in turn. Once used up, removals succeed.</param>
    public FakeTxtProvider(DNSProviderKind kind, params bool[] removals)
    {
        Kind = kind;
        foreach (var removal in removals)
        {
            _removals.Enqueue(removal);
        }
    }

    /// <inheritdoc />
    public DNSProviderKind Kind { get; }

    /// <inheritdoc />
    public string Label => Kind.ToString();

    /// <inheritdoc />
    public string Hint => string.Empty;

    /// <inheritdoc />
    public ProviderFields Fields => new();

    /// <inheritdoc />
    public bool SupportsIPv6 => true;

    /// <summary>Gets each TXT record published, as its name and value.</summary>
    public List<(string Name, string Value)> Published { get; } = new();

    /// <summary>Gets each TXT record removal asked for, as its name and value.</summary>
    public List<(string Name, string Value)> Removed { get; } = new();

    /// <inheritdoc />
    public Task<DNSUpdateResult> UpdateAsync(DNSRecord record, DetectedIP ip, CancellationToken cancellationToken)
        => Task.FromResult(DNSUpdateResult.Ok("good"));

    /// <inheritdoc />
    public Task<(bool Ok, string Message)> SetTxtAsync(DNSRecord record, string name, string value, CancellationToken cancellationToken)
    {
        Published.Add((name, value));
        return Task.FromResult((true, string.Empty));
    }

    /// <inheritdoc />
    public Task<bool> RemoveTxtAsync(DNSRecord record, string name, string value, CancellationToken cancellationToken)
    {
        Removed.Add((name, value));
        return Task.FromResult(_removals.Count == 0 || _removals.Dequeue());
    }
}
