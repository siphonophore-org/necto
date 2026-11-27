using System.Collections.Generic;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Notifications;

/// <summary>
/// The keys of every event Pages sends, so Notifications can list them. A store sends them only while its
/// Notify administrators is on.
/// </summary>
public static class CustomPageEvents
{
    /// <summary>Pages added records to a store, summed over a burst.</summary>
    public const string RecordsAdded = "pages.records_added";

    /// <summary>Records were deleted from a store, summed over a burst.</summary>
    public const string RecordsRemoved = "pages.records_removed";

    /// <summary>An administrator cleared a store.</summary>
    public const string StoreCleared = "pages.store_cleared";

    /// <summary>
    /// Gets every event Pages sends, in the order Notifications lists them.
    /// </summary>
    public static IReadOnlyList<NectoEventInfo> All { get; } = new[]
    {
        new NectoEventInfo(RecordsAdded, TranslationKeys.PagesActivityAddedMany),
        new NectoEventInfo(RecordsRemoved, TranslationKeys.PagesActivityRemovedMany),
        new NectoEventInfo(StoreCleared, TranslationKeys.PagesActivityStoreCleared)
    };
}
