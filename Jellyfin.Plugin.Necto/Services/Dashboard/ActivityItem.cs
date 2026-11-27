using System;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Necto.Services.Dashboard;

/// <summary>
/// One of Necto's activity log entries, as the Dashboard lists it.
/// </summary>
/// <param name="Date">When it was written, in UTC.</param>
/// <param name="Name">Its name.</param>
/// <param name="Overview">Its overview, or null.</param>
/// <param name="Type">Its type, such as Necto.Proxy.AddressBlocked.</param>
/// <param name="Severity">How serious it is.</param>
/// <param name="UserId">The user it is about, or empty.</param>
public sealed record ActivityItem(DateTime Date, string Name, string? Overview, string Type, LogLevel Severity, Guid UserId);
