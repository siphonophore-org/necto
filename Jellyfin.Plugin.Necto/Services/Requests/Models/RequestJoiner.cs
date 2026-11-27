using System;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// Someone who joined another person's request for a title they want too, to be told as its requester is.
/// </summary>
public class RequestJoiner
{
    /// <summary>Gets or sets who joined; empty when the viewer may not see who.</summary>
    public Guid UserId { get; set; }

    /// <summary>Gets or sets their name; empty when the viewer may not see who.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Gets or sets when they joined.</summary>
    public DateTime JoinedUtc { get; set; }
}
