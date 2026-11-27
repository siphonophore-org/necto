using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Necto.Configuration.CustomPages;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.CustomPages;

/// <summary>
/// Checks the Pages settings before they are saved. The page checks the same rules, but settings can come from
/// any API client, so the server holds them too.
/// </summary>
public static partial class ConfigurationValidator
{
    /// <summary>The largest decoded image accepted, the same as the page's upload limit.</summary>
    public const int MaxAssetBytes = 5 * 1024 * 1024;

    /// <summary>
    /// Gets the slugs no page may take, since a route of the pages' own addresses starts with them.
    /// </summary>
    public static IReadOnlySet<string> ReservedSlugs { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "asset",
        "store",
        "stores",
        "configuration"
    };

    /// <summary>
    /// Checks the settings, and throws when they must not be saved.
    /// </summary>
    /// <param name="settings">The settings about to be saved.</param>
    /// <param name="language">The language of the refusal, such as de, or <c>null</c> for US English.</param>
    /// <exception cref="ArgumentException">When a slug or name repeats or is reserved, or a tier, route, image, or store is invalid.</exception>
    public static void Validate(CustomPagesSettings settings, string? language = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ValidatePages(settings.Pages ?? new List<CustomPage>(), language);
        ValidateAssets(settings.Assets ?? new List<PageAsset>(), language);
        ValidateStores(settings.Stores ?? new List<PageStore>(), language);
    }

    /// <summary>
    /// Builds a refusal in the administrator's language, since the page shows it as the reason a save failed.
    /// </summary>
    private static ArgumentException Refuse(string? language, string key, params object?[] args)
        => new(StolonTranslations.Format(key, language, args));

    private static void ValidatePages(IEnumerable<CustomPage> pages, string? language)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var page in pages)
        {
            // A tier no enum value names matches no content route yet outranks every image tier.
            if (!Enum.IsDefined(page.Visibility))
            {
                throw Refuse(language, TranslationKeys.PagesErrorPageTier, page.Slug);
            }

            // Groups name a page by id, so two pages with one id would share every group's grant.
            if (!string.IsNullOrEmpty(page.Id) && !ids.Add(page.Id))
            {
                throw Refuse(language, TranslationKeys.PagesErrorPageId, page.Slug);
            }

            // A page with an empty or invalid slug is an unreachable draft, so only reachable slugs must be unique.
            if (!PageService.IsValidSlug(page.Slug))
            {
                continue;
            }

            if (ReservedSlugs.Contains(page.Slug))
            {
                throw Refuse(language, TranslationKeys.PagesErrorSlugReserved, page.Slug);
            }

            if (!seen.Add(page.Slug))
            {
                throw Refuse(language, TranslationKeys.PagesErrorPageDuplicate, page.Slug);
            }

            ValidateApiRoutes(page, language);
        }
    }

    private static void ValidateApiRoutes(CustomPage page, string? language)
    {
        if (page.ApiRoutes is null || page.ApiRoutes.Count == 0)
        {
            return;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var route in page.ApiRoutes)
        {
            // The name is a path segment and the serverFetch argument, so it takes a slug's safe characters.
            if (!PageService.IsValidSlug(route.Name))
            {
                throw Refuse(language, TranslationKeys.PagesErrorRouteName, page.Slug, route.Name);
            }

            if (!names.Add(route.Name))
            {
                throw Refuse(language, TranslationKeys.PagesErrorRouteDuplicate, page.Slug, route.Name);
            }

            // A fixed absolute http or https target keeps this from being an open proxy.
            if (!Uri.TryCreate(route.Url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw Refuse(language, TranslationKeys.PagesErrorRouteUrl, route.Name, page.Slug);
            }
        }
    }

    private static void ValidateAssets(IEnumerable<PageAsset> assets, string? language)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in assets)
        {
            if (!PageService.IsValidAssetName(asset.Name))
            {
                throw Refuse(language, TranslationKeys.PagesErrorAssetName, asset.Name);
            }

            if (!seen.Add(asset.Name))
            {
                throw Refuse(language, TranslationKeys.PagesErrorAssetDuplicate, asset.Name);
            }

            if (!Enum.IsDefined(asset.Visibility))
            {
                throw Refuse(language, TranslationKeys.PagesErrorAssetTier, asset.Name);
            }

            // The type goes into a response header and a data: address, so it is held to a bare type and subtype.
            if (asset.ContentType is null || !ContentTypePattern().IsMatch(asset.ContentType))
            {
                throw Refuse(language, TranslationKeys.PagesErrorAssetType, asset.Name);
            }

            if (asset.DataBase64 is null)
            {
                throw Refuse(language, TranslationKeys.PagesErrorAssetMissing, asset.Name);
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(asset.DataBase64);
            }
            catch (FormatException)
            {
                throw Refuse(language, TranslationKeys.PagesErrorAssetBase64, asset.Name);
            }

            if (bytes.Length > MaxAssetBytes)
            {
                throw Refuse(language, TranslationKeys.PagesErrorAssetSize, asset.Name);
            }
        }
    }

    private static void ValidateStores(IEnumerable<PageStore> stores, string? language)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var store in stores)
        {
            // The name is an address segment and the store's file name, so it takes a slug's safe characters.
            if (!StoreService.IsValidStoreName(store.Name))
            {
                throw Refuse(language, TranslationKeys.PagesErrorStoreName, store.Name);
            }

            if (!seen.Add(store.Name))
            {
                throw Refuse(language, TranslationKeys.PagesErrorStoreDuplicate, store.Name);
            }

            if (!Enum.IsDefined(store.ReadAccess))
            {
                throw Refuse(language, TranslationKeys.PagesErrorStoreReadTier, store.Name);
            }

            if (!Enum.IsDefined(store.WriteAccess))
            {
                throw Refuse(language, TranslationKeys.PagesErrorStoreWriteTier, store.Name);
            }

            if (!Enum.IsDefined(store.ReadScope))
            {
                throw Refuse(language, TranslationKeys.PagesErrorStoreScope, store.Name);
            }

            if (store.MaxRecords < 1 || store.MaxRecords > PageStore.RecordCeiling)
            {
                throw Refuse(language, TranslationKeys.PagesErrorStoreLimit, PageStore.RecordCeiling, store.Name);
            }

            // 0 keeps records forever, and a negative retention would expire each record as it is written.
            if (store.RetentionDays < 0 || store.RetentionDays > PageStore.RetentionDayCeiling)
            {
                throw Refuse(language, TranslationKeys.PagesErrorStoreRetention, PageStore.RetentionDayCeiling, store.Name);
            }
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9!#$&^_.+-]+/[A-Za-z0-9!#$&^_.+-]+$")]
    private static partial Regex ContentTypePattern();
}
