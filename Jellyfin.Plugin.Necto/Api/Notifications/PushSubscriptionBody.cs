namespace Jellyfin.Plugin.Necto.Api.Notifications;

/// <summary>
/// A browser's subscription to notifications, as Stolon's subscribeWebPush sends it.
/// </summary>
public class PushSubscriptionBody
{
    /// <summary>Gets or sets the address of the browser's push service.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Gets or sets the browser's keys.</summary>
    public PushSubscriptionKeys? Keys { get; set; }

    /// <summary>Gets or sets a short name for the browser, such as Safari · iPhone.</summary>
    public string Label { get; set; } = string.Empty;
}
