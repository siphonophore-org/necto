using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// The title a details page asks about.
/// </summary>
public class DetailsLookup
{
    /// <summary>Gets or sets the type.</summary>
    public RequestType Type { get; set; }

    /// <summary>Gets or sets the title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the year.</summary>
    public int? Year { get; set; }

    /// <summary>Gets or sets the provider ids.</summary>
    public Dictionary<string, string> ProviderIds { get; set; } = new();

    /// <summary>Gets or sets the library item, when the library has it.</summary>
    public Guid? LibraryItemId { get; set; }
}
