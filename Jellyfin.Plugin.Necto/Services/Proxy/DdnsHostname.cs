using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
using Jellyfin.Plugin.Necto.Api.Ddns;
namespace Jellyfin.Plugin.Necto.Services.Proxy;

/// <summary>
/// A hostname a DDNS record keeps pointed at this server, offered as a choice for the proxy's hostnames.
/// </summary>
/// <param name="Hostname">The normalized hostname or wildcard.</param>
/// <param name="Record">The record's friendly name, or its hostname when unnamed.</param>
/// <param name="Enabled">Whether the record is enabled.</param>
/// <param name="RecordId">The id of the record the name comes from.</param>
public sealed record DdnsHostname(string Hostname, string Record, bool Enabled, string RecordId);
