using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
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
    /// Every tool's events class lists its events in the one shape the catalog reads, and the catalog holds them all.
    /// True: Requests, Issues, Storage, Pages, Proxy, DDNS, WireGuard, Scripts, Invites, and Notifications
    /// events each show in the channel and group pickers.
    /// False: a tool whose list had another shape would be left out of every picker, and its users would get nothing.
    /// </summary>
    [Fact]
    public void All_HoldsEveryEventsClass()
    {
        var keys = NectoEventCatalog.All.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        var lists = typeof(NectoEventCatalog).Assembly.GetTypes()
            .Where(t => t.IsAbstract && t.IsSealed && t.Name.EndsWith("Events", StringComparison.Ordinal))
            .Select(t => (Type: t, All: t.GetProperty("All", BindingFlags.Public | BindingFlags.Static)))
            .Where(x => x.All is not null)
            .ToList();

        Assert.Equal(
            new[] { "CustomPageEvents", "DdnsEvents", "InvitesEvents", "IssueEvents", "NotificationsEvents", "ProxyEvents", "RequestEvents", "ScriptsEvents", "StorageEvents", "WireGuardEvents" },
            lists.Select(x => x.Type.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.All(lists, x => Assert.Equal(typeof(IReadOnlyList<NectoEventInfo>), x.All!.PropertyType));
        Assert.All(lists.SelectMany(x => (IReadOnlyList<NectoEventInfo>)x.All!.GetValue(null)!), e => Assert.Contains(e.Key, keys));
    }

    /// <summary>
    /// Every event key a tool declares as a constant is in the catalog.
    /// True: a key added to an events class without adding it to the list fails here.
    /// False: a tool could send an event no group or channel can pick.
    /// </summary>
    [Fact]
    public void All_HoldsEveryDeclaredKey()
    {
        var keys = NectoEventCatalog.All.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);

        var declared = DeclaredKeys();

        Assert.NotEmpty(declared);
        Assert.All(declared, k => Assert.True(keys.Contains(k.Value), k.Key + " = " + k.Value + " is in no event list"));
    }

    /// <summary>
    /// Every event a tool sends through INotifier is built with a key from an events class, and that key is listed.
    /// True: the source has no event sent under a literal or another class's key that Notifications cannot list.
    /// False: an event would be sent that only channels with Every event receive.
    /// </summary>
    [Fact]
    public void Senders_UseListedKeys()
    {
        var keys = NectoEventCatalog.All.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        var declared = DeclaredKeys();
        var problems = new List<string>();
        var sites = 0;

        foreach (var file in Directory.EnumerateFiles(FindPluginFolder(), "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
        {
            var text = File.ReadAllText(file);
            foreach (Match site in Regex.Matches(text, @"new NectoEvent\(\s*([^,]+),"))
            {
                sites++;
                var key = site.Groups[1].Value.Trim();
                var name = Path.GetFileName(file);
                if (key.StartsWith('"'))
                {
                    if (!keys.Contains(key.Trim('"')))
                    {
                        problems.Add(name + ": " + key);
                    }
                }
                else if (Regex.Match(key, @"^(\w+Events)\.(\w+)$") is { Success: true } member)
                {
                    if (!declared.ContainsKey(member.Value) || !keys.Contains(declared[member.Value]))
                    {
                        problems.Add(name + ": " + key);
                    }
                }
                else if (!Regex.IsMatch(key, @"^\w+Events\.\w+\(") && !Regex.IsMatch(text, @"\b\w+Events\.\w+\b"))
                {
                    // A key passed in, such as from a switch, must come from an events class in the same file.
                    problems.Add(name + ": " + key);
                }
            }
        }

        Assert.True(sites > 10, "Too few senders were found, so the scan is not reading the source.");
        Assert.Empty(problems);
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

    // Every event key declared as a constant on an events class, by Class.Name.
    private static Dictionary<string, string> DeclaredKeys()
        => typeof(NectoEventCatalog).Assembly.GetTypes()
            .Where(t => t.IsAbstract && t.IsSealed && t.Name.EndsWith("Events", StringComparison.Ordinal))
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                .Select(f => (Name: t.Name + "." + f.Name, Value: (string)f.GetRawConstantValue()!)))
            .Where(x => x.Value.Contains('.', StringComparison.Ordinal) && Plugin.ToolKeys.Contains(x.Value[..x.Value.IndexOf('.', StringComparison.Ordinal)]))
            .ToDictionary(x => x.Name, x => x.Value, StringComparer.Ordinal);

    private static string FindPluginFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Jellyfin.Plugin.Necto");
            if (File.Exists(Path.Combine(candidate, "Jellyfin.Plugin.Necto.csproj")))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("The Jellyfin.Plugin.Necto folder was not found above the test run.");
    }
}
