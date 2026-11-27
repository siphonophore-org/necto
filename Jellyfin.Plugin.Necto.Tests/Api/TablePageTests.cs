using System.Linq;
using Jellyfin.Plugin.Necto.Api;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Api;

/// <summary>
/// A tool's table keeps the rows in the state its filter names, searches each row's text, and pages them in order.
/// </summary>
public class TablePageTests
{
    private sealed record Row(string Name, string Line, string State);

    private static readonly Row[] All =
    {
        new("Kids", "3 members", "On"),
        new("Family", "Kids and parents", "Off"),
        new("Friends", "2 members", "On"),
        new("Guests", "No members", "On")
    };

    private static string[] Names(Siphonophore.Stolon.PagedResult<Row> page) => page.Items.Select(r => r.Name).ToArray();

    /// <summary>
    /// The filter, the search, and the page all apply, and All or nothing keeps every row.
    /// True: a table shows the right rows, a page at a time, in their order.
    /// False: a filter could show rows in another state, or a search miss a row by its line.
    /// </summary>
    [Fact]
    public void Of_FiltersByStateSearchesAndPages()
    {
        var page = (Siphonophore.Stolon.PagedQuery? q) => TablePage.Of(All, q, r => r.State, r => new[] { r.Name, r.Line });

        Assert.Equal(4, page(null).TotalCount);
        Assert.Equal(4, page(new() { Filter = "All" }).TotalCount);
        Assert.Equal(new[] { "Kids", "Friends", "Guests" }, Names(page(new() { Filter = "on" })));
        Assert.Empty(page(new() { Filter = "Nope" }).Items);
        Assert.Equal(new[] { "Kids", "Family" }, Names(page(new() { Search = "KIDS" })));
        Assert.Equal(new[] { "Kids" }, Names(page(new() { Filter = "On", Search = "kid" })));

        var second = page(new() { Skip = 1, Take = 2 });
        Assert.Equal(new[] { "Family", "Friends" }, Names(second));
        Assert.Equal(4, second.TotalCount);
        Assert.True(second.HasMore);
    }
}
