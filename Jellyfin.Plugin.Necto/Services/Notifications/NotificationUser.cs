using System;

namespace Jellyfin.Plugin.Necto.Services.Notifications;

/// <summary>
/// What routing needs to know about one Jellyfin user.
/// </summary>
/// <param name="Id">The user.</param>
/// <param name="IsAdministrator">Whether the user is an administrator.</param>
/// <param name="IsDisabled">Whether the account is disabled, which gets nothing.</param>
public sealed record NotificationUser(Guid Id, bool IsAdministrator, bool IsDisabled);
