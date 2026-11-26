using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Services.Issues;

namespace Jellyfin.Plugin.Necto.Services.Issues.Models;

/// <summary>
/// What an administrator does with an issue.
/// </summary>
public enum IssueAction
{
    /// <summary>Agree something is wrong: Reported becomes Confirmed.</summary>
    Confirm = 0,

    /// <summary>Mark it fixed: an open issue becomes Resolved.</summary>
    Resolve = 1,

    /// <summary>Find nothing to fix: an open issue becomes Declined.</summary>
    Decline = 2,

    /// <summary>Open a closed issue again: it becomes Reported.</summary>
    Reopen = 3
}
