using System;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;

namespace Jellyfin.Plugin.Necto.Api.Proxy;

/// <summary>
/// One row of the Proxy tool's table: a route, its certificate's status, the state the table's filter goes by, and the
/// detail its line names.
/// </summary>
/// <param name="Route">The route, with its certificate password replaced by the kept marker.</param>
/// <param name="Status">Its certificate's status, or <c>null</c> when the proxy has none for it.</param>
/// <param name="State">Off, Untrusted, Http, Incomplete, or Https, from <see cref="RouteRow.StateOf"/>.</param>
/// <param name="Detail">What is not finished or soon due, from <see cref="RouteRow.DetailOf"/>, or empty.</param>
public sealed record RouteRow(ProxyRoute Route, RouteStatus? Status, string State, string Detail)
{
    /// <summary>The state of a route that is off.</summary>
    public const string Off = "Off";

    /// <summary>The state of a route to Jellyfin while Jellyfin does not trust the proxy.</summary>
    public const string Untrusted = "Untrusted";

    /// <summary>The state of a route that answers over HTTP only, with no certificate source.</summary>
    public const string Http = "Http";

    /// <summary>The state of a route with something set but not finished, such as a certificate still to come.</summary>
    public const string Incomplete = "Incomplete";

    /// <summary>The state of a route that serves HTTPS with its certificate.</summary>
    public const string Https = "Https";

    /// <summary>The detail of a route whose certificate expires within 14 days.</summary>
    public const string ExpiresSoon = "ExpiresSoon";

    /// <summary>The detail of a route whose certificate Let's Encrypt is issuing.</summary>
    public const string Requesting = "Requesting";

    /// <summary>The detail of a route still waiting for its certificate.</summary>
    public const string Waiting = "Waiting";

    /// <summary>The detail of a route whose certificate failed to load or be issued.</summary>
    public const string Failed = "Failed";

    /// <summary>The detail of a route to Jellyfin while Jellyfin restarts to trust the proxy.</summary>
    public const string Restarting = "Restarting";

    /// <summary>
    /// Tells whether a route forwards to Jellyfin, which has no upstream of its own.
    /// </summary>
    /// <param name="route">The route.</param>
    /// <returns>True for a route to Jellyfin.</returns>
    public static bool ToJellyfin(ProxyRoute route) => string.IsNullOrWhiteSpace(route?.Upstream);

    /// <summary>
    /// Gives the state a route's row ends with: Off while it is off, Untrusted for a route to Jellyfin while Jellyfin
    /// does not trust the proxy, HTTP for a route with no certificate, HTTPS once its certificate is loaded, and
    /// Incomplete while its certificate or Jellyfin's trust is still to come.
    /// </summary>
    /// <param name="route">The route.</param>
    /// <param name="status">Its status, or <c>null</c>.</param>
    /// <param name="trust">How far Jellyfin trusts the proxy.</param>
    /// <returns>The state.</returns>
    public static string StateOf(ProxyRoute route, RouteStatus? status, TakeoverPhase trust)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (!route.Enabled)
        {
            return Off;
        }

        if (ToJellyfin(route) && trust != TakeoverPhase.Managed)
        {
            return trust == TakeoverPhase.PendingRestart ? Incomplete : Untrusted;
        }

        if (route.Certificate?.Mode == CertificateMode.None)
        {
            return Http;
        }

        return status?.Certificate.Loaded == true && status.Acme.Issuing != true ? Https : Incomplete;
    }

    /// <summary>
    /// Gives what a route's line adds to its state: Jellyfin restarting to trust the proxy, a certificate being
    /// requested, failed, still to come, or expiring within 14 days, or nothing.
    /// </summary>
    /// <param name="route">The route.</param>
    /// <param name="status">Its status, or <c>null</c>.</param>
    /// <param name="trust">How far Jellyfin trusts the proxy.</param>
    /// <param name="now">The time now, which an expiry is measured from.</param>
    /// <returns>The detail, or empty.</returns>
    public static string DetailOf(ProxyRoute route, RouteStatus? status, TakeoverPhase trust, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (!route.Enabled || route.Certificate?.Mode == CertificateMode.None)
        {
            return ToJellyfin(route) && route.Enabled && trust == TakeoverPhase.PendingRestart ? Restarting : string.Empty;
        }

        if (ToJellyfin(route) && trust == TakeoverPhase.PendingRestart)
        {
            return Restarting;
        }

        if (status?.Acme.Issuing == true)
        {
            return Requesting;
        }

        if (status?.Certificate.Loaded == true)
        {
            var due = status.Certificate.NotAfterUtc;
            return due.HasValue && due.Value - now < TimeSpan.FromDays(14) ? ExpiresSoon : string.Empty;
        }

        return !string.IsNullOrEmpty(status?.Certificate.Error) || !string.IsNullOrEmpty(status?.Acme.LastError) ? Failed : Waiting;
    }
}
