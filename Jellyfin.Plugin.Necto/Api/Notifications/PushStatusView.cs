using System.Collections.Generic;

namespace Jellyfin.Plugin.Necto.Api.Notifications;

/// <summary>
/// Where web push stands, for Troubleshooting.
/// </summary>
/// <param name="HasKeys">Whether the VAPID keys are set up.</param>
/// <param name="Browsers">The browsers users turned notifications on in.</param>
/// <param name="Held">How many Jellyfin messages wait for an app to come online.</param>
public sealed record PushStatusView(bool HasKeys, IReadOnlyList<PushBrowserView> Browsers, int Held);
