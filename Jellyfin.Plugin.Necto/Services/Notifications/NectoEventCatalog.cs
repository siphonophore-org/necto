using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Jellyfin.Plugin.Necto.Services.Notifications;

/// <summary>
/// Lists every event a tool may send, from each static <c>*Events</c> class in the plugin with a
/// <c>public static IReadOnlyList&lt;NectoEventInfo&gt; All</c>, read once by reflection.
/// </summary>
public static class NectoEventCatalog
{
    private static readonly Lazy<IReadOnlyList<NectoEventInfo>> _all = new(() => Discover(typeof(NectoEventCatalog).Assembly));

    /// <summary>Gets every event, by tool in sidebar order and then as each tool lists them.</summary>
    public static IReadOnlyList<NectoEventInfo> All => _all.Value;

    /// <summary>
    /// Finds an event by its key.
    /// </summary>
    /// <param name="key">The event's key.</param>
    /// <returns>The event, or <c>null</c> when no tool lists it.</returns>
    public static NectoEventInfo? Find(string? key)
        => All.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.Ordinal));

    /// <summary>
    /// Finds every event list in an assembly.
    /// </summary>
    /// <param name="assembly">The assembly to search.</param>
    /// <returns>The events, each key once, by tool in sidebar order.</returns>
    public static IReadOnlyList<NectoEventInfo> Discover(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var events = new List<NectoEventInfo>();
        var lists = assembly.GetTypes()
            .Where(t => t.IsAbstract && t.IsSealed && t.Name.EndsWith("Events", StringComparison.Ordinal))
            .OrderBy(t => t.FullName, StringComparer.Ordinal);
        foreach (var type in lists)
        {
            var property = type.GetProperty("All", BindingFlags.Public | BindingFlags.Static);
            if (property?.GetValue(null) is IReadOnlyList<NectoEventInfo> list)
            {
                events.AddRange(list.Where(e => e is not null));
            }
        }

        var order = Plugin.ToolKeys.ToList();
        return events
            .GroupBy(e => e.Key, StringComparer.Ordinal)
            .Select(g => g.First())
            .Select((e, i) => (Event: e, Index: i))
            .OrderBy(x => Rank(order, x.Event.Tool))
            .ThenBy(x => x.Index)
            .Select(x => x.Event)
            .ToList();
    }

    private static int Rank(List<string> order, string tool)
    {
        var at = order.IndexOf(tool);
        return at < 0 ? int.MaxValue : at;
    }
}
