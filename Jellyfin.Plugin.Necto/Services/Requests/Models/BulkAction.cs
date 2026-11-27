using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// An approve, decline or remove applied to several requests at once from the dashboard.
/// </summary>
public class BulkAction
{
    /// <summary>Gets or sets the request ids.</summary>
    public List<Guid> Ids { get; set; } = new();

    /// <summary>Gets or sets the reason, for a decline.</summary>
    public string? Reason { get; set; }
}
