using System.Linq;
using Jellyfin.Plugin.Necto.Services.Invites;
using Jellyfin.Plugin.Necto.Services.Notifications;
using Siphonophore.Stolon;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Notifications;

/// <summary>
/// The catalog finds every tool's event list by its name, keeps each key once, and orders the events by tool.
/// </summary>
public class NectoEventCatalogTests
{
    /// <summary>
    /// The catalog holds the events of every *Events class.
    /// True: a tool that adds an event list is listed on Notifications without any other change.
    /// False: a tool's events would be missing from the channel and group pickers.
    /// </summary>
    [Fact]
    public void All_FindsEveryEventsClass()
    {
        var keys = NectoEventCatalog.All.Select(e => e.Key).ToList();

        Assert.Contains(NotificationsEvents.Test, keys);
        Assert.All(InvitesEvents.All, e => Assert.Contains(e.Key, keys));
    }

    /// <summary>
    /// Every key is listed once, and every key starts with a tool's key.
    /// True: an event picked in a group or channel names one event.
    /// False: two tools could send one key, or a key could name no tool.
    /// </summary>
    [Fact]
    public void All_KeysAreUniqueAndNamedByTool()
    {
        var keys = NectoEventCatalog.All.Select(e => e.Key).ToList();

        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.All(NectoEventCatalog.All, e => Assert.Contains(e.Tool, Plugin.ToolKeys));
    }

    /// <summary>
    /// Events are ordered by the tool's place in the sidebar.
    /// True: the picker lists Invites before Notifications, as the sidebar does.
    /// False: the picker would list events in a random order.
    /// </summary>
    [Fact]
    public void All_IsInSidebarOrder()
    {
        var tools = NectoEventCatalog.All.Select(e => Plugin.ToolKeys.ToList().IndexOf(e.Tool)).ToList();

        Assert.Equal(tools.OrderBy(t => t), tools);
    }

    /// <summary>
    /// Every event's name has a string.
    /// True: each event shows a name on the pages.
    /// False: an event would show its key.
    /// </summary>
    [Fact]
    public void All_NamesHaveStrings()
    {
        Assert.All(NectoEventCatalog.All, e => Assert.NotEqual(e.NameKey, StolonTranslations.Get(e.NameKey, null)));
    }

    /// <summary>
    /// A key no list holds is not found.
    /// True: an unknown key is treated as an administrators' event.
    /// False: an unknown key could reach a user.
    /// </summary>
    [Fact]
    public void Find_UnknownKey_IsNull()
    {
        Assert.Null(NectoEventCatalog.Find("nothing.here"));
        Assert.Null(NectoEventCatalog.Find(null));
    }
}
