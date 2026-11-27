using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using Xunit;
using SearchResult = Jellyfin.Plugin.Necto.Services.Requests.Models.SearchResult;

namespace Jellyfin.Plugin.Necto.Tests.Services.Requests;

/// <summary>
/// Requests and search results become Jellyfin items: library titles built together in one batch and kept in order,
/// and titles outside the library under one stable id.
/// </summary>
public class RequestItemsTests
{
    /// <summary>
    /// A title outside the library keeps one id however its ids are spelled, by type, ids, and name.
    /// True: the page links the same title to the same address from every list.
    /// False: a title would get a new id each time, or two titles would share one.
    /// </summary>
    [Fact]
    public void VirtualId_SameTitle_KeepsOneId()
    {
        var dune = RequestItems.VirtualId("Movie", new Dictionary<string, string> { ["Tmdb"] = "438631", ["Imdb"] = "tt1160419" }, "Dune|2021");

        Assert.Equal(dune, RequestItems.VirtualId("Movie", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["tmdb"] = "438631" }, "Dune (2021)"));
        Assert.NotEqual(dune, RequestItems.VirtualId("Series", new Dictionary<string, string> { ["Tmdb"] = "438631" }, "Dune|2021"));
        Assert.NotEqual(dune, RequestItems.VirtualId("Movie", new Dictionary<string, string> { ["Imdb"] = "tt1160419" }, "Dune|2021"));
        Assert.Equal(
            RequestItems.VirtualId("Person", new Dictionary<string, string>(), "Keanu Reeves"),
            RequestItems.VirtualId("Person", new Dictionary<string, string>(), " keanu reeves "));
        Assert.NotEqual(Guid.Empty, dune);
        Assert.Equal('3', dune.ToString()[14]);
    }

    /// <summary>
    /// A page's library items are built in one batch, in the requests' order, each the same as built alone.
    /// True: a page of requests costs one call to Jellyfin's item builder, and search results one more.
    /// False: each request would build its item alone, or the list would come back out of order.
    /// </summary>
    [Fact]
    public void For_PageOfRequests_BuildsInOneBatchInOrder()
    {
        var movies = Enumerable.Range(1, 4).Select(i => new Movie { Id = Guid.NewGuid(), Name = "Movie " + i }).ToList();
        var singles = 0;
        var batches = 0;
        var library = InterfaceStub.Of<ILibraryManager>(
            ("GetItemList", a => movies.Where(m => ((InternalItemsQuery)a[0]!).ItemIds.Contains(m.Id)).Reverse().ToList()),
            ("GetItemById", a => movies.FirstOrDefault(m => m.Id == (Guid)a[0]!)));
        var dtos = InterfaceStub.Of<IDtoService>(
            ("GetBaseItemDtos", a =>
            {
                batches++;
                Assert.True((bool)a[4]!);
                return ((IReadOnlyList<BaseItem>)a[0]!).Reverse().Select(i => new BaseItemDto { Id = i.Id, Name = i.Name }).ToList();
            }),
            ("GetBaseItemDto", a =>
            {
                singles++;
                return new BaseItemDto { Id = ((BaseItem)a[0]!).Id, Name = ((BaseItem)a[0]!).Name };
            }));
        var items = new RequestItems(dtos, library, InterfaceStub.Of<IServerApplicationHost>(("get_SystemId", _ => RequestHarness.ServerId)));
        var requests = new List<MediaRequest>
        {
            new() { Id = Guid.NewGuid(), Title = "Movie 3", LibraryItemId = movies[2].Id },
            new() { Id = Guid.NewGuid(), Title = "Wanted", Status = RequestStatus.Pending },
            new() { Id = Guid.NewGuid(), Title = "Movie 1", LibraryItemId = movies[0].Id },
            new() { Id = Guid.NewGuid(), Title = "Movie 4", LibraryItemId = movies[3].Id },
            new() { Id = Guid.NewGuid(), Title = "Movie 3 again", LibraryItemId = movies[2].Id }
        };

        var built = items.For(requests, null);

        Assert.Equal(new[] { "Movie 3", "Wanted", "Movie 1", "Movie 4", "Movie 3" }, built.Select(i => i.Name));
        Assert.Equal(requests.Select(r => r.Id), built.Select(i => i.RequestId));
        Assert.Equal(requests[1].Id, built[1].Id);
        Assert.Equal("Pending", built[1].Status);
        Assert.NotSame(built[0], built[4]);
        Assert.Equal(1, batches);
        Assert.Equal(0, singles);
        Assert.Equal(requests.Select(r => items.For(r, null).Name), built.Select(i => i.Name));

        singles = 0;
        var results = new List<SearchResult>
        {
            new() { Title = "Movie 2", LibraryItemId = movies[1].Id },
            new() { Title = "Elsewhere", ProviderIds = new() { ["Tmdb"] = "9" } },
            new() { Title = "Movie 4", LibraryItemId = movies[3].Id }
        };
        var found = items.For(results, null);

        Assert.Equal(new[] { "Movie 2", "Elsewhere", "Movie 4" }, found.Select(i => i.Name));
        Assert.Equal(movies[1].Id, found[0].Id);
        Assert.Same(results[1], found[1].Result);
        Assert.Equal(2, batches);
        Assert.Equal(0, singles);
    }

    /// <summary>
    /// A title the batch leaves out is still built on its own.
    /// True: a request whose item the batch read missed still shows as its library title.
    /// False: the request would show without its item.
    /// </summary>
    [Fact]
    public void For_TitleTheBatchLeavesOut_IsBuiltAlone()
    {
        var movie = new Movie { Id = Guid.NewGuid(), Name = "Alone" };
        var library = InterfaceStub.Of<ILibraryManager>(("GetItemById", a => (Guid)a[0]! == movie.Id ? movie : null));
        var dtos = InterfaceStub.Of<IDtoService>(("GetBaseItemDto", a => new BaseItemDto { Id = ((BaseItem)a[0]!).Id, Name = ((BaseItem)a[0]!).Name }));
        var items = new RequestItems(dtos, library, InterfaceStub.Of<IServerApplicationHost>());

        var built = items.For(new List<MediaRequest> { new() { Id = Guid.NewGuid(), Title = "Alone", LibraryItemId = movie.Id } }, null);

        Assert.Equal("Alone", Assert.Single(built).Name);
        Assert.Equal(movie.Id, built[0].Id);
    }
}
