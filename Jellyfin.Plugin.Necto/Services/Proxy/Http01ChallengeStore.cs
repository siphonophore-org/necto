using System;
using System.Collections.Concurrent;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Services.Proxy.Reachability;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
using Jellyfin.Plugin.Necto.Api.Ddns;

namespace Jellyfin.Plugin.Necto.Services.Proxy;

/// <summary>
/// Pending ACME HTTP-01 challenges, answered by the proxy's HTTP listener while Let's Encrypt checks an
/// address.
/// </summary>
public sealed class Http01ChallengeStore
{
    private readonly ConcurrentDictionary<string, string> _pending = new(StringComparer.Ordinal);

    /// <summary>
    /// Publishes a challenge response.
    /// </summary>
    /// <param name="token">The challenge token from the URL.</param>
    /// <param name="keyAuthorization">The key authorization to answer with.</param>
    public void Add(string token, string keyAuthorization) => _pending[token] = keyAuthorization;

    /// <summary>
    /// Withdraws a challenge once validation is over.
    /// </summary>
    /// <param name="token">The challenge token.</param>
    public void Remove(string token) => _pending.TryRemove(token, out _);

    /// <summary>
    /// Looks up the answer for a token.
    /// </summary>
    /// <param name="token">The challenge token.</param>
    /// <param name="keyAuthorization">The answer.</param>
    /// <returns>True when the token is pending.</returns>
    public bool TryGet(string token, out string keyAuthorization)
    {
        if (_pending.TryGetValue(token, out var value))
        {
            keyAuthorization = value;
            return true;
        }

        keyAuthorization = string.Empty;
        return false;
    }
}
