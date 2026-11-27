namespace Jellyfin.Plugin.Necto.Configuration.Issues;

/// <summary>
/// The Issues settings. Who may report and manage issues is set on groups.
/// </summary>
public class IssueSettings
{
    /// <summary>Gets or sets a value indicating whether users may report issues with library items at /issues.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets how many days resolved, declined, and rescinded issues are kept. 0 keeps them.</summary>
    public int RetentionDays { get; set; } = 90;

    /// <summary>Gets or sets how many days an issue limit counts reports back over.</summary>
    public int LimitDays { get; set; } = 1;

    /// <summary>Gets or sets a value indicating whether the Issues link is in the web client's menu, which Necto sets itself.</summary>
    public bool MenuLinkAdded { get; set; }
}
