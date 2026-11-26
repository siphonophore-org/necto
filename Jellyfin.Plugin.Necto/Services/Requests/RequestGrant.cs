using System.Collections.Generic;

namespace Jellyfin.Plugin.Necto.Services.Requests;

/// <summary>
/// What one user may do in Requests and Issues, every setting decided from their groups and the quota windows.
/// </summary>
/// <param name="CanRequest">Whether they may request at all.</param>
/// <param name="Movies">Whether they may request movies.</param>
/// <param name="Shows">Whether they may request series.</param>
/// <param name="AutoApproveMovies">Whether their movie requests are approved as soon as they are made.</param>
/// <param name="AutoApproveShows">Whether their series requests are approved as soon as they are made.</param>
/// <param name="SeeAllRequests">Whether they see everyone's requests.</param>
/// <param name="SeeRequesters">Whether they see who made other people's requests.</param>
/// <param name="Unreleased">Whether they may request titles not yet released.</param>
/// <param name="MaxOpenRequests">The most open requests at once, 0 for no limit.</param>
/// <param name="MovieQuotaLimit">The movie quota, 0 for no limit.</param>
/// <param name="MovieQuotaDays">The movie quota's window in days.</param>
/// <param name="ShowQuotaLimit">The series quota, 0 for no limit.</param>
/// <param name="ShowQuotaDays">The series quota's window in days.</param>
/// <param name="MaxSeasons">The most seasons of one series they may request, 0 for no limit.</param>
/// <param name="MovieProfiles">The Radarr profiles they pick from, or empty for every one.</param>
/// <param name="ShowProfiles">The Sonarr profiles they pick from, or empty for every one.</param>
/// <param name="ReportIssues">Whether they may report issues with library items.</param>
/// <param name="IssueQuotaLimit">How many issues they may report or join in the window, 0 for no limit.</param>
/// <param name="IssueQuotaDays">The issue limit's window in days.</param>
/// <param name="ManageRequests">Whether they may approve, decline, and remove anyone's requests.</param>
/// <param name="ManageIssues">Whether they may confirm, resolve, decline, and reopen issues.</param>
public sealed record RequestGrant(
    bool CanRequest,
    bool Movies,
    bool Shows,
    bool AutoApproveMovies,
    bool AutoApproveShows,
    bool SeeAllRequests,
    bool SeeRequesters,
    bool Unreleased,
    int MaxOpenRequests,
    int MovieQuotaLimit,
    int MovieQuotaDays,
    int ShowQuotaLimit,
    int ShowQuotaDays,
    int MaxSeasons,
    IReadOnlyList<string> MovieProfiles,
    IReadOnlyList<string> ShowProfiles,
    bool ReportIssues = true,
    int IssueQuotaLimit = 0,
    int IssueQuotaDays = 1,
    bool ManageRequests = false,
    bool ManageIssues = false);
