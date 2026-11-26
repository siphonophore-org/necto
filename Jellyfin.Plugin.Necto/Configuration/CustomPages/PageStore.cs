namespace Jellyfin.Plugin.Necto.Configuration.CustomPages;

/// <summary>
/// A named collection of JSON records that pages read and write through <c>/pages/store/{name}</c>. A store belongs to
/// no page, so a page that collects records and a page that reports on them share it. Its tiers alone decide access.
/// The records live in a file under Jellyfin's data folder, since visitors write them.
/// </summary>
public class PageStore
{
    /// <summary>The most records a store may be set to hold.</summary>
    public const int RecordCeiling = 100000;

    /// <summary>The longest retention a store may be set to, about ten years.</summary>
    public const int RetentionDayCeiling = 3650;

    /// <summary>Gets or sets the store's name, its address segment and the helper's first argument.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the store answers. A disabled store answers 404.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the lowest tier that may read the store. A caller below it reads nothing.</summary>
    public PageVisibility ReadAccess { get; set; } = PageVisibility.Admin;

    /// <summary>
    /// Gets or sets how much a reader sees. <see cref="StoreScope.Own"/> shows each viewer only the records they
    /// created. Administrators see every record either way.
    /// </summary>
    public StoreScope ReadScope { get; set; } = StoreScope.All;

    /// <summary>Gets or sets the lowest tier that may add a record. Anyone opens it to visitors who are not signed in.</summary>
    public PageVisibility WriteAccess { get; set; } = PageVisibility.User;

    /// <summary>Gets or sets a value indicating whether a signed in writer may change and delete the records they created.</summary>
    public bool AllowEditOwn { get; set; }

    /// <summary>Gets or sets how many records the store holds before it refuses writes.</summary>
    public int MaxRecords { get; set; } = 1000;

    /// <summary>Gets or sets how many days a record is kept, counted from when it was created. 0 keeps records forever.</summary>
    public int RetentionDays { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether records added and removed are written to the activity log and sent to
    /// Notifications, summed into one entry per burst.
    /// </summary>
    public bool NotifyOnWrite { get; set; }
}
