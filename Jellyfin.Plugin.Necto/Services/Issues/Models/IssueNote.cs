using System;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Services.Issues;

namespace Jellyfin.Plugin.Necto.Services.Issues.Models;

/// <summary>
/// One note on an issue: a reporter's, a joiner's, or an administrator's.
/// </summary>
public class IssueNote
{
    /// <summary>Gets or sets who wrote it.</summary>
    public Guid UserId { get; set; }

    /// <summary>Gets or sets their name, kept for when the account is gone.</summary>
    public string User { get; set; } = string.Empty;

    /// <summary>Gets or sets what they wrote.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Gets or sets when they wrote it.</summary>
    public DateTime Time { get; set; }

    /// <summary>Gets or sets a value indicating whether an administrator wrote it with a decision, rather than a reporter with their report.</summary>
    public bool Admin { get; set; }
}
