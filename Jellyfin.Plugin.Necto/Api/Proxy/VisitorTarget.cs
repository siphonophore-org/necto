namespace Jellyfin.Plugin.Necto.Api.Proxy;

/// <summary>
/// What a visitor action acts on, sent in the body so an address never travels in a URL.
/// </summary>
/// <param name="Value">An internet address or network, or a two letter country code.</param>
public sealed record VisitorTarget(string Value);
