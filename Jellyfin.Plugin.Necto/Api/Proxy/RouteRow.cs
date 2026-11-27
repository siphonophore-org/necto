using System;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy;

namespace Jellyfin.Plugin.Necto.Api.Proxy;

/// <summary>
/// One row of the Proxy tool's table: a route, its certificate's status, and the state the table's filter goes by.
/// </summary>
/// <param name="Route">The route, with its certificate password replaced by the kept marker.</param>
/// <param name="Status">Its certificate's status, or <c>null</c> when the proxy has none for it.</param>
/// <param name="State">One of the states <see cref="RouteRow.StateOf"/> gives.</param>
public sealed record RouteRow(ProxyRoute Route, RouteStatus? Status, string State)
{
    /// <summary>The state of a route that is off.</summary>
    public const string Off = "Off";

    /// <summary>The state of a route that serves HTTP only.</summary>
    public const string NoCertificate = "NoCertificate";

    /// <summary>The state of a route whose certificate Let's Encrypt is issuing.</summary>
    public const string Requesting = "Requesting";

    /// <summary>The state of a route whose certificate expires within 14 days.</summary>
    public const string ExpiresSoon = "ExpiresSoon";

    /// <summary>The state of a route whose certificate is loaded.</summary>
    public const string Ready = "Ready";

    /// <summary>The state of a route whose certificate failed to load or be issued.</summary>
    public const string Failed = "Failed";

    /// <summary>The state of a route still waiting for its certificate.</summary>
    public const string Waiting = "Waiting";

    /// <summary>
    /// Gives the state a route's row ends with: Off while the route is off, else its certificate's state.
    /// </summary>
    /// <param name="route">The route.</param>
    /// <param name="status">Its status, or <c>null</c>.</param>
    /// <param name="now">The time now, which an expiry is measured from.</param>
    /// <returns>The state.</returns>
    public static string StateOf(ProxyRoute route, RouteStatus? status, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (!route.Enabled)
        {
            return Off;
        }

        if (route.Certificate?.Mode == CertificateMode.None)
        {
            return NoCertificate;
        }

        if (status?.Acme.Issuing == true)
        {
            return Requesting;
        }

        if (status?.Certificate.Loaded == true)
        {
            var due = status.Certificate.NotAfterUtc;
            return due.HasValue && due.Value - now < TimeSpan.FromDays(14) ? ExpiresSoon : Ready;
        }

        return !string.IsNullOrEmpty(status?.Certificate.Error) || !string.IsNullOrEmpty(status?.Acme.LastError) ? Failed : Waiting;
    }
}
