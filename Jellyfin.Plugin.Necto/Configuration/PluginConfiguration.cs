using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.Accounts;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Groups;
using Jellyfin.Plugin.Necto.Configuration.Scripts;
using Jellyfin.Plugin.Necto.Configuration.WireGuard;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Necto.Configuration;

/// <summary>
/// The settings of the Necto plugin. Each tool keeps its settings in its own section.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Gets or sets the groups, without Everyone.</summary>
    public List<Group> Groups { get; set; } = new();

    /// <summary>Gets or sets the Everyone group, which applies to users in no other group.</summary>
    public Group Everyone { get; set; } = NewEveryone();

    /// <summary>Gets or sets the Groups settings.</summary>
    public GroupSettings GroupSettings { get; set; } = new();

    /// <summary>Gets or sets the account settings.</summary>
    public AccountSettings AccountSettings { get; set; } = new();

    /// <summary>Gets or sets the provider each user had before password rules moved them onto Necto's.</summary>
    public List<ProviderEnrollment> ProviderEnrollments { get; set; } = new();

    /// <summary>Gets or sets the WireGuard settings, peers, and connections.</summary>
    public WireGuardSettings WireGuard { get; set; } = new();

    /// <summary>Gets or sets the scripts and the Scripts settings.</summary>
    public ScriptSettings Scripts { get; set; } = new();
    /// <summary>Gets or sets the Storage settings.</summary>
    public Storage.StorageConfiguration Storage { get; set; } = new();

    /// <summary>Gets or sets the Custom pages settings: pages, images, and stores.</summary>
    public CustomPages.CustomPagesSettings CustomPages { get; set; } = new();

    /// <summary>Gets or sets the Notifications settings and channels.</summary>
    public Notifications.NotificationSettings Notifications { get; set; } = new();

    /// <summary>Gets or sets the Invites settings.</summary>
    public Invites.InviteSettings InviteSettings { get; set; } = new();

    /// <summary>Gets or sets the invites.</summary>
    public List<Invites.Invite> Invites { get; set; } = new();
    /// <summary>Gets or sets the DDNS records and settings.</summary>
    public DdnsSettings Ddns { get; set; } = new();

    /// <summary>Gets or sets the proxy's routes and settings.</summary>
    public ProxySettings Proxy { get; set; } = new();
    /// <summary>Gets or sets the Requests settings.</summary>
    public RequestSettings Requests { get; set; } = new();

    /// <summary>Gets or sets the Issues settings.</summary>
    public IssueSettings Issues { get; set; } = new();

    /// <summary>
    /// Builds the Everyone group as a fresh install has it, which gives nothing.
    /// </summary>
    /// <returns>The Everyone group.</returns>
    public static Group NewEveryone() => new() { Id = Group.EveryoneId, Name = "Everyone" };
}
