using System;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Services.Issues;

namespace Jellyfin.Plugin.Necto.Services.Issues.Models;

/// <summary>
/// Someone who reported an issue, or joined it, with what they say is wrong.
/// </summary>
public class IssueReporter
{
    /// <summary>Gets or sets who reported it.</summary>
    public Guid UserId { get; set; }

    /// <summary>Gets or sets their name, kept for when the account is gone.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Gets or sets what they say is wrong.</summary>
    public IssueType Type { get; set; }

    /// <summary>Gets or sets when they reported or joined it.</summary>
    public DateTime ReportedUtc { get; set; }
}
