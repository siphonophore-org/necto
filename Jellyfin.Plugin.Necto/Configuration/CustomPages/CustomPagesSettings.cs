using System.Collections.Generic;

namespace Jellyfin.Plugin.Necto.Configuration.CustomPages;

/// <summary>
/// Everything Pages keeps in the settings: its pages, images, stores, and the values a new store starts with.
/// </summary>
public class CustomPagesSettings
{
    /// <summary>Gets or sets a value indicating whether Pages is on. While it is off, the /pages addresses answer not found.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the pages.</summary>
    public List<CustomPage> Pages { get; set; } = new();

    /// <summary>Gets or sets the images, served at <c>/pages/asset/{name}</c> or embedded into pages.</summary>
    public List<PageAsset> Assets { get; set; } = new();

    /// <summary>Gets or sets the stores. Only their definitions live here. Their records are files under the data folder.</summary>
    public List<PageStore> Stores { get; set; } = new();

    /// <summary>Gets or sets the record limit a new store starts with.</summary>
    public int NewStoreMaxRecords { get; set; } = 1000;

    /// <summary>Gets or sets the retention in days a new store starts with. 0 keeps records forever.</summary>
    public int NewStoreRetentionDays { get; set; }

    /// <summary>Gets or sets a value indicating whether a new store starts with Notify administrators on.</summary>
    public bool NewStoreNotifyOnWrite { get; set; }
}
