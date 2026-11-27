using System;
using System.Collections.Generic;
using System.Linq;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Api;

/// <summary>
/// Picks one page of a tool's table: the rows in the state the filter names whose text holds the search.
/// </summary>
public static class TablePage
{
    /// <summary>
    /// Picks one page of rows, in their order.
    /// </summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <param name="all">Every row.</param>
    /// <param name="query">The page, the search, and a state in the filter, or All for every row.</param>
    /// <param name="stateOf">Gives a row's state, which the filter matches by name, ignoring case.</param>
    /// <param name="textOf">Gives the text of a row the search looks in, ignoring case.</param>
    /// <returns>The page, with how many rows match.</returns>
    public static PagedResult<T> Of<T>(IEnumerable<T> all, PagedQuery? query, Func<T, string> stateOf, Func<T, IEnumerable<string?>> textOf)
    {
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(stateOf);
        ArgumentNullException.ThrowIfNull(textOf);
        query ??= new PagedQuery();
        var filter = (query.Filter ?? string.Empty).Trim();
        var every = filter.Length == 0 || filter.Equals("All", StringComparison.OrdinalIgnoreCase);
        var search = (query.Search ?? string.Empty).Trim();
        var matching = all
            .Where(row => every || string.Equals(stateOf(row), filter, StringComparison.OrdinalIgnoreCase))
            .Where(row => search.Length == 0 || textOf(row).Any(text => text is not null && text.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return matching.ToPagedResult(query);
    }
}
