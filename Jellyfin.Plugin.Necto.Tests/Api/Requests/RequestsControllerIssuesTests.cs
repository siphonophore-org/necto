using System;
using System.Collections.Generic;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Necto.Api.Requests;
using Jellyfin.Plugin.Necto.Services.Issues.Models;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using MediaBrowser.Controller.Entities.Movies;
using Microsoft.AspNetCore.Mvc;
using Siphonophore.Stolon;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Api.Requests;

/// <summary>
/// The Issues routes answer only while Issues are on, show an issue's file only to managers, and narrow the managers'
/// list by state, type, and search. They read the loaded plugin's settings, so they run on their own.
/// </summary>
[Collection(ServicesCollection.Name)]
public sealed class RequestsControllerIssuesTests : IDisposable
{
    private readonly RequestHarness _h = new();
    private readonly User _viewer;
    private readonly User _other;
    private readonly User _admin;
    private readonly Movie _movie;

    public RequestsControllerIssuesTests()
    {
        _h.Plugin.MutateConfiguration(c => { c.Issues.Enabled = true; return false; });
        _viewer = _h.AddUser("viewer");
        _other = _h.AddUser("other");
        _admin = _h.AddUser("admin", admin: true);
        _movie = Movie("The Lighthouse Keeper", 2019);
    }

    public void Dispose() => _h.Dispose();

    /// <summary>
    /// Every issue route answers not found while Issues are off.
    /// True: a server with Issues off has no issue API.
    /// False: issues could be reported or listed while the tool is off.
    /// </summary>
    [Fact]
    public void IssueRoutes_IssuesOff_AnswerNotFound()
    {
        _h.Plugin.MutateConfiguration(c => { c.Issues.Enabled = false; return false; });
        var api = _h.Api(_viewer);

        Assert.IsType<NotFoundObjectResult>(api.IssueSuggestions().Result);
        Assert.IsType<NotFoundObjectResult>(api.IssueSearch("keeper").Result);
        Assert.IsType<NotFoundObjectResult>(api.IssueItem(_movie.Id).Result);
        Assert.IsType<NotFoundObjectResult>(api.ReportIssue(new IssueSubmit { ItemId = _movie.Id, Type = IssueType.Video }).Result);
        Assert.IsType<NotFoundObjectResult>(_h.Api(_admin).ListIssues(null, null, null, null, null, null).Result);
    }

    /// <summary>
    /// Only managers see an issue's file.
    /// True: a reporter never learns where the server keeps its files.
    /// False: an issue would show its file's path to anyone who reported it.
    /// </summary>
    [Fact]
    public void GetIssue_File_IsShownOnlyToManagers()
    {
        var api = _h.Api(_viewer);

        var reported = Assert.IsType<OkObjectResult>(api.ReportIssue(new IssueSubmit { ItemId = _movie.Id, Type = IssueType.Video }).Result).Value as Issue;
        var view = Assert.IsType<OkObjectResult>(api.IssueItem(_movie.Id).Result).Value as IssueView;
        var listed = Assert.IsType<OkObjectResult>(_h.Api(_admin).GetIssue(reported!.Id).Result).Value as Issue;

        Assert.Null(reported.Path);
        Assert.Null(view!.Mine!.Path);
        Assert.Equal(_movie.Path, listed!.Path);
    }

    /// <summary>
    /// The managers' list is narrowed by state, type, and search, and counted by state.
    /// True: the Issues page's filters show the issues they name.
    /// False: a filter would show issues of another state or type.
    /// </summary>
    [Fact]
    public void IssueStates_Filter_NarrowsTheList()
    {
        var first = _h.Issues.Report(_h.Access(_viewer), new IssueSubmit { ItemId = _movie.Id, Type = IssueType.Audio }).Issue!;
        _h.Issues.Report(_h.Access(_other), new IssueSubmit { ItemId = Movie("Paper Harbor", 2021).Id, Type = IssueType.Subtitles });

        _h.Issues.Withdraw(_h.Access(_viewer), first.Id);

        Assert.Equal(1, _h.Db.IssuePage(new PagedQuery(), RequestsController.IssueStates("Open")).TotalCount);
        Assert.Equal(2, _h.Db.IssuePage(new PagedQuery(), RequestsController.IssueStates("All")).TotalCount);
        Assert.Equal(1, _h.Db.IssuePage(new PagedQuery(), RequestsController.IssueStates("Rescinded,Resolved")).TotalCount);
        Assert.Equal(1, _h.Db.IssuePage(new PagedQuery(), null, IssueType.Subtitles).TotalCount);
        Assert.Equal(1, _h.Db.IssuePage(new PagedQuery { Search = "harbor" }).TotalCount);
        Assert.Equal(1, _h.Db.IssuePage(new PagedQuery { Search = "other" }).TotalCount);
        Assert.Equal(1, _h.Db.CountIssuesByState()[IssueState.Rescinded]);
    }

    private Movie Movie(string name, int year)
    {
        var movie = new Movie
        {
            Id = Guid.NewGuid(),
            Name = name,
            ProductionYear = year,
            Path = "/media/movies/" + name + "/" + name + ".mkv",
            Size = 1000,
            DateModified = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "603", ["Imdb"] = "tt0133093" }
        };
        _h.Library.Add(movie);
        return movie;
    }
}
