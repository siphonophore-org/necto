using System;

namespace Jellyfin.Plugin.Necto.Api.Notifications;

/// <summary>
/// One browser a user turned notifications on in, as Troubleshooting lists it.
/// </summary>
/// <param name="UserName">The user's name, or empty for a user the server no longer has.</param>
/// <param name="Label">The device's short name, such as Safari · iPhone.</param>
/// <param name="CreatedUtc">When it signed up.</param>
public sealed record PushBrowserView(string UserName, string Label, DateTime CreatedUtc);
