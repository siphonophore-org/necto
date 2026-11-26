using Jellyfin.Plugin.Necto.Configuration.Accounts;

namespace Jellyfin.Plugin.Necto.Configuration.Groups;

/// <summary>
/// Everything a group gives its members, one section per tool. Necto sets no permission anywhere else.
/// </summary>
public class GroupPermissions
{
    /// <summary>Gets or sets the account rules.</summary>
    public AccountRules Accounts { get; set; } = new();

    /// <summary>Gets or sets what members may do in Requests.</summary>
    public RequestPermissions Requests { get; set; } = new();

    /// <summary>Gets or sets what members may do in Issues.</summary>
    public IssuePermissions Issues { get; set; } = new();

    /// <summary>Gets or sets what members may do in Invites.</summary>
    public InvitePermissions Invites { get; set; } = new();

    /// <summary>Gets or sets what members may have in WireGuard.</summary>
    public WireGuardPermissions WireGuard { get; set; } = new();

    /// <summary>Gets or sets the scripts that run for members.</summary>
    public ScriptPermissions Scripts { get; set; } = new();

    /// <summary>Gets or sets the custom pages members may open.</summary>
    public CustomPagePermissions CustomPages { get; set; } = new();

    /// <summary>Gets or sets the events members receive.</summary>
    public NotificationPermissions Notifications { get; set; } = new();
}
