using System;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Services.Issues;

namespace Jellyfin.Plugin.Necto.Services.Issues.Models;

/// <summary>
/// A report, a join, or a change to one: the item, what is wrong, and a note.
/// </summary>
public class IssueSubmit
{
    /// <summary>Gets or sets the library item, for a new report.</summary>
    public Guid ItemId { get; set; }

    /// <summary>Gets or sets what is wrong.</summary>
    public IssueType Type { get; set; }

    /// <summary>Gets or sets the note, required for <see cref="IssueType.Other"/>.</summary>
    public string? Note { get; set; }
}
