using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.Necto.Configuration;

/// <summary>
/// The tools that turn on and off, each with the settings section that holds its Enabled switch. A tool that is off
/// leaves the sidebar, the dashboard, and the group editor, and its routes, tasks, and services do nothing. Its
/// stored data stays. Dashboard, Groups, Notifications, Settings, and Troubleshooting are always on.
/// </summary>
public static class ToolSwitches
{
    /// <summary>The Requests tool's key.</summary>
    public const string Requests = "requests";

    /// <summary>The Issues tool's key.</summary>
    public const string Issues = "issues";

    /// <summary>The Invites tool's key.</summary>
    public const string Invites = "invites";

    /// <summary>The Proxy tool's key.</summary>
    public const string Proxy = "proxy";

    /// <summary>The DDNS tool's key.</summary>
    public const string Ddns = "ddns";

    /// <summary>The VPN tool's key.</summary>
    public const string WireGuard = "wireguard";

    /// <summary>The Scripts tool's key.</summary>
    public const string Scripts = "scripts";

    /// <summary>The Pages tool's key.</summary>
    public const string Pages = "pages";

    /// <summary>The Storage tool's key.</summary>
    public const string Storage = "storage";

    /// <summary>
    /// Gets every tool with a switch, in sidebar order. The pages give the same tools a <c>visibleWhen</c> in
    /// <c>nectoTools</c> in necto_shared.js, and a test checks the two agree.
    /// </summary>
    public static IReadOnlyList<ToolSwitch> All { get; } = new ToolSwitch[]
    {
        new(Requests, nameof(PluginConfiguration.Requests), c => c.Requests?.Enabled == true),
        new(Issues, nameof(PluginConfiguration.Issues), c => c.Issues?.Enabled == true),
        new(Invites, nameof(PluginConfiguration.InviteSettings), c => c.InviteSettings?.Enabled == true),
        new(Proxy, nameof(PluginConfiguration.Proxy), c => c.Proxy?.Enabled == true),
        new(Ddns, nameof(PluginConfiguration.Ddns), c => c.Ddns?.Enabled == true),
        new(WireGuard, nameof(PluginConfiguration.WireGuard), c => c.WireGuard?.Enabled == true),
        new(Scripts, nameof(PluginConfiguration.Scripts), c => c.Scripts?.Enabled == true),
        new(Pages, nameof(PluginConfiguration.CustomPages), c => c.CustomPages?.Enabled == true),
        new(Storage, nameof(PluginConfiguration.Storage), c => c.Storage?.Enabled == true)
    };

    /// <summary>Gets whether a tool is on in a configuration. A tool with no switch is always on.</summary>
    /// <param name="config">The configuration.</param>
    /// <param name="key">The tool's key, such as requests.</param>
    /// <returns><c>true</c> while the tool is on.</returns>
    public static bool IsOn(PluginConfiguration config, string key)
    {
        ArgumentNullException.ThrowIfNull(config);
        var tool = All.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.Ordinal));
        return tool is null || tool.IsOn(config);
    }

    /// <summary>
    /// Gets whether a tool is on in the loaded plugin's settings. With no plugin loaded, as for a service built on its
    /// own in a test, there is no switch to read and every tool is on.
    /// </summary>
    /// <param name="key">The tool's key, such as requests.</param>
    /// <returns><c>true</c> while the tool is on.</returns>
    public static bool IsOn(string key) => Plugin.Instance?.ReadConfiguration(c => IsOn(c, key)) ?? true;
}

/// <summary>A tool with an Enabled switch.</summary>
/// <param name="Key">The tool's key, as the sidebar lists it.</param>
/// <param name="Section">The configuration section that holds its Enabled switch.</param>
/// <param name="IsOn">Reads whether it is on.</param>
public sealed record ToolSwitch(string Key, string Section, Func<PluginConfiguration, bool> IsOn);
