using System;

namespace Jellyfin.Plugin.Necto.Api.Requests;

/// <summary>
/// Marks a route that makes, changes, or joins a request, which answers not found while Requests is off.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class MakesRequestAttribute : Attribute
{
}
