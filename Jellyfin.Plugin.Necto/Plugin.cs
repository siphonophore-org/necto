using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Services.Groups;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto;

/// <summary>
/// The Necto plugin. It registers one page in the dashboard menu, which holds every tool in Stolon's tool app.
/// </summary>
public sealed class Plugin : PluginBase<Plugin, PluginConfiguration>
{
    /// <summary>
    /// Gets the key of every tool, in the order the sidebar lists them. Each tool is the page <c>necto_&lt;key&gt;</c> and its script.
    /// </summary>
    public static IReadOnlyList<string> ToolKeys { get; } = new[]
    {
        "dashboard",
        "requests",
        "issues",
        "invites",
        "groups",
        "proxy",
        "ddns",
        "wireguard",
        "scripts",
        "pages",
        "storage",
        "notifications",
        "settings",
        "troubleshooting"
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Gives the data and cache paths.</param>
    /// <param name="xmlSerializer">Reads and writes the settings file.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
    }

    /// <inheritdoc />
    public override string Name => "Necto";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("a8b5ecb0-fc00-428b-85e8-74b85820166d");

    /// <inheritdoc />
    public override string Description => "Tools to keep your Jellyfin server reachable, secure and healthy.";

    /// <summary>
    /// Normalizes the groups of a configuration before it is saved, so a save from any page keeps Everyone, every
    /// section, and only groups that exist in the settings that name one.
    /// </summary>
    /// <param name="incoming">The configuration about to be saved.</param>
    /// <param name="stored">The configuration in effect.</param>
    protected override void OnConfigurationSaving(PluginConfiguration incoming, PluginConfiguration stored)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        GroupNormalizer.Normalize(incoming);
    }

    /// <inheritdoc />
    public override IEnumerable<PluginPageInfo> GetPages()
    {
        var ns = typeof(Plugin).Namespace;

        // The one page the menu opens. It holds every tool below in a sidebar and swaps them in place.
        yield return new PluginPageInfo
        {
            Name = "necto",
            EmbeddedResourcePath = $"{ns}.Web.necto.html",
            MenuSection = "server",
            DisplayName = "Necto",
            EnableInMainMenu = true,
            MenuIcon = "admin_panel_settings"
        };

        yield return new PluginPageInfo
        {
            Name = "necto.js",
            EmbeddedResourcePath = $"{ns}.Web.necto.js"
        };

        foreach (var key in ToolKeys)
        {
            yield return new PluginPageInfo
            {
                Name = $"necto_{key}",
                EmbeddedResourcePath = $"{ns}.Web.necto_{key}.html"
            };

            yield return new PluginPageInfo
            {
                Name = $"necto_{key}.js",
                EmbeddedResourcePath = $"{ns}.Web.necto_{key}.js"
            };
        }

        yield return new PluginPageInfo
        {
            Name = "necto_shared.css",
            EmbeddedResourcePath = $"{ns}.Web.necto_shared.css"
        };

        yield return new PluginPageInfo
        {
            Name = "necto_shared.js",
            EmbeddedResourcePath = $"{ns}.Web.necto_shared.js"
        };

        // The Groups sections of Settings and Troubleshooting, which those pages load to fill the registry.
        yield return new PluginPageInfo
        {
            Name = "necto_groups_sections.js",
            EmbeddedResourcePath = $"{ns}.Web.necto_groups_sections.js"
        };

        // The modules of Notifications, Invites, and the group templates: their sections, the group editor's template
        // sections, and the scripts of the user pages and the browser notification switch they share.
        foreach (var module in new[] { "necto_notifications_sections", "necto_invites_sections", "necto_groups_templates", "necto_invite_signup", "necto_invites_member", "necto_push" })
        {
            yield return new PluginPageInfo
            {
                Name = module + ".js",
                EmbeddedResourcePath = $"{ns}.Web.{module}.js"
            };
        }

        foreach (var page in GetSharedPages("necto"))
        {
            yield return page;
        }
    }
}
