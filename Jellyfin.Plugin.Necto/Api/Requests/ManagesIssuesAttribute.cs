using System;

namespace Jellyfin.Plugin.Necto.Api.Requests;

/// <summary>
/// Marks a route only administrators and members of a group that may manage issues may call.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class ManagesIssuesAttribute : Attribute
{
}
