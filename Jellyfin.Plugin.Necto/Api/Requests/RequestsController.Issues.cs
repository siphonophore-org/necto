using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Siphonophore.Stolon;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Api.Requests;

/// <summary>
/// The issue routes: what the issue page at /issues/ calls as the signed-in user, and what the Issues tab calls as an
/// administrator. Every one answers 404 while issues are off.
/// </summary>
public partial class RequestsController
{
    /// <summary>Lists what the signed-in user played last, newest first, for the issue page's suggestions.</summary>
    /// <returns>The items.</returns>
    [HttpGet("/Issues/Suggestions")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<IssueItemList> IssueSuggestions()
    {
        if (IssuesOff() is { } off)
        {
            return off;
        }

        return GetAccess() is { ReportIssues: true } access ? Ok(new IssueItemList { Items = _issues!.Suggestions(access) }) : Forbid();
    }

    /// <summary>Finds the playable items the signed-in user may see whose name matches a search.</summary>
    /// <param name="searchTerm">What they typed.</param>
    /// <returns>The items.</returns>
    [HttpGet("/Issues/Search")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<IssueItemList> IssueSearch([FromQuery] string? searchTerm)
    {
        if (IssuesOff() is { } off)
        {
            return off;
        }

        return GetAccess() is { ReportIssues: true } access ? Ok(new IssueItemList { Items = _issues!.Search(access, searchTerm) }) : Forbid();
    }

    /// <summary>Gets an item with its issues: the open one the user reported or joined, someone else's, and the closed ones.</summary>
    /// <param name="itemId">The library item.</param>
    /// <returns>The item and its issues.</returns>
    [HttpGet("/Issues/Items/{itemId:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<IssueView> IssueItem([FromRoute] Guid itemId)
    {
        if (IssuesOff() is { } off)
        {
            return off;
        }

        if (GetAccess() is not { ReportIssues: true } access)
        {
            return Forbid();
        }

        return _issues!.Find(itemId, access) is { } item ? Ok(_issues.View(access, item)) : NotFound(new { Error = StolonTranslations.Get(TranslationKeys.IssuesErrorItemNotFound, Language) });
    }

    /// <summary>Reports an issue with an item, refused when the item already has an open one.</summary>
    /// <param name="body">The item, what is wrong, and a note.</param>
    /// <returns>The issue.</returns>
    [HttpPost("/Issues")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<Issue> ReportIssue([FromBody] IssueSubmit body)
        => IssueChange(access => body is null ? (null, StolonTranslations.Get(TranslationKeys.IssuesErrorType, Language)) : _issues!.Report(access, body));

    /// <summary>Joins someone else's open issue.</summary>
    /// <param name="issueId">The issue.</param>
    /// <param name="body">What is wrong, and a note.</param>
    /// <returns>The issue.</returns>
    [HttpPost("/Issues/{issueId:guid}/Join")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<Issue> JoinIssue([FromRoute] Guid issueId, [FromBody] IssueSubmit body)
        => IssueChange(access => _issues!.Join(access, issueId, body ?? new IssueSubmit()));

    /// <summary>Changes what the user says is wrong with an open issue they reported or joined, and their note.</summary>
    /// <param name="issueId">The issue.</param>
    /// <param name="body">What is wrong, and the note.</param>
    /// <returns>The issue.</returns>
    [HttpPost("/Issues/{issueId:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<Issue> EditIssue([FromRoute] Guid issueId, [FromBody] IssueSubmit body)
        => IssueChange(access => _issues!.Edit(access, issueId, body ?? new IssueSubmit()), editing: true);

    /// <summary>
    /// Takes the user off an open issue: rescinds it when they are its only reporter, or takes them out of it otherwise.
    /// </summary>
    /// <param name="issueId">The issue.</param>
    /// <returns>The issue as it now stands.</returns>
    [HttpDelete("/Issues/{issueId:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<Issue> WithdrawIssue([FromRoute] Guid issueId)
        => IssueChange(access => _issues!.Withdraw(access, issueId), editing: true);

    /// <summary>Lists issues for the Issues tab, most recently changed first.</summary>
    /// <param name="query">The page (skip and take) and search.</param>
    /// <param name="startIndex">Where the page starts, as Jellyfin names it.</param>
    /// <param name="limit">How many, as Jellyfin names it.</param>
    /// <param name="searchTerm">Title, show or reporter.</param>
    /// <param name="state">A state, several separated by commas, Open, or All.</param>
    /// <param name="type">Only issues where a reporter says this is wrong.</param>
    /// <returns>The page.</returns>
    [HttpGet("/Issues/All")]
    [Authorize]
    [ManagesIssues]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<RequestsQueryResult<Issue>> ListIssues([FromQuery] PagedQuery? query, [FromQuery] int? startIndex, [FromQuery] int? limit, [FromQuery] string? searchTerm, [FromQuery] string? state, [FromQuery] string? type)
    {
        if (IssuesOff() is { } off)
        {
            return off;
        }

        var paging = Paging(query, startIndex, limit, searchTerm, state);
        var page = _db.IssuePage(paging, IssueStates(paging.Filter), Enum.TryParse<IssueType>(type, true, out var t) && Enum.IsDefined(t) ? t : null);
        var (skip, _) = RequestDatabase.Window(paging);
        return Ok(new RequestsQueryResult<Issue>(skip, page.TotalCount, page.Items.ToList()));
    }

    /// <summary>Counts the issues in each state, for the Issues tab.</summary>
    /// <returns>The counts, by state name.</returns>
    [HttpGet("/Issues/Stats")]
    [Authorize]
    [ManagesIssues]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<Dictionary<string, int>> IssueStats()
    {
        if (IssuesOff() is { } off)
        {
            return off;
        }

        var counts = _db.CountIssuesByState();
        return Ok(Enum.GetValues<IssueState>().ToDictionary(s => s.ToString(), s => counts.GetValueOrDefault(s)));
    }

    /// <summary>Gets one issue, with its file, for an administrator.</summary>
    /// <param name="issueId">The issue.</param>
    /// <returns>The issue.</returns>
    [HttpGet("/Issues/{issueId:guid}")]
    [Authorize]
    [ManagesIssues]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<Issue> GetIssue([FromRoute] Guid issueId)
    {
        if (IssuesOff() is { } off)
        {
            return off;
        }

        return _db.GetIssue(issueId) is { } issue ? Ok(issue) : NotFound();
    }

    /// <summary>Confirms, resolves, declines or reopens an issue, with an optional note its reporters see.</summary>
    /// <param name="issueId">The issue.</param>
    /// <param name="verb">Confirm, Resolve, Decline or Reopen.</param>
    /// <param name="body">The note.</param>
    /// <returns>The issue.</returns>
    [HttpPost("/Issues/{issueId:guid}/{verb}")]
    [Authorize]
    [ManagesIssues]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<Issue> DecideIssue([FromRoute] Guid issueId, [FromRoute] string verb, [FromBody] IssueDecision? body)
    {
        if (IssuesOff() is { } off)
        {
            return off;
        }

        if (!Enum.TryParse<IssueAction>(verb, true, out var what) || !Enum.IsDefined(what) || int.TryParse(verb, out _))
        {
            return NotFound();
        }

        if (GetAccess(asCaller: true) is not { } admin)
        {
            return Forbid();
        }

        var (issue, error) = _issues!.Decide(admin, issueId, what, body?.Note);
        return issue is null ? BadRequest(new { Error = error }) : Ok(issue);
    }

    /// <summary>Gets where an administrator opens an issue's title in Radarr or Sonarr, or no content when neither has it.</summary>
    /// <param name="issueId">The issue.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The page and the server's name.</returns>
    [HttpGet("/Issues/{issueId:guid}/ArrLink")]
    [Authorize]
    [ManagesIssues]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ArrLink>> IssueArrLink([FromRoute] Guid issueId, CancellationToken cancellationToken)
    {
        if (IssuesOff() is { } off)
        {
            return off;
        }

        if (_db.GetIssue(issueId) is not { } issue)
        {
            return NotFound();
        }

        return await _issues!.ArrLinkAsync(issue, cancellationToken).ConfigureAwait(false) is { } link ? Ok(link) : NoContent();
    }

    // The 404 every issue route answers while issues are off, or null while they are on.
    private NotFoundObjectResult? IssuesOff()
        => _issues is not null && IssueService.Enabled ? null : NotFound(new { Error = StolonTranslations.Get(TranslationKeys.IssuesErrorOff, Language) });

    // A change a user makes to an issue: refused while issues are off, to anyone who may not report (unless changing or
    // withdrawing their own), and to an API key, which is no one.
    private ActionResult<Issue> IssueChange(Func<UserAccess, (Issue? Issue, string? Error)> change, bool editing = false)
    {
        if (IssuesOff() is { } off)
        {
            return off;
        }

        if (GetAccess() is not { } access || (!access.ReportIssues && !editing) || !access.IssuesEnabled)
        {
            return Forbid();
        }

        if (access.UserId == Guid.Empty)
        {
            return BadRequest(new { Error = "Name the user to report for with userId." });
        }

        var (issue, error) = change(access);
        return issue is null ? BadRequest(new { Error = error }) : Ok(IssueService.ForViewer(issue, access));
    }

    // The states a list filter names: Open, All (or nothing) for every state, or state names separated by commas.
    internal static IReadOnlyCollection<IssueState>? IssueStates(string? filter)
    {
        filter = (filter ?? string.Empty).Trim();
        if (filter.Length == 0 || filter.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (filter.Equals("Open", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { IssueState.Reported, IssueState.Confirmed };
        }

        return filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => Enum.TryParse<IssueState>(s, true, out var state) && Enum.IsDefined(state) && !int.TryParse(s, out _) ? state : (IssueState?)null)
            .OfType<IssueState>()
            .Distinct()
            .ToList();
    }
}
