using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Services.Issues;

namespace Jellyfin.Plugin.Necto.Services.Issues.Models;

/// <summary>
/// An administrator's note with a decision on an issue.
/// </summary>
public class IssueDecision
{
    /// <summary>Gets or sets the note, shown to the reporters with the issue. Optional.</summary>
    public string? Note { get; set; }
}
