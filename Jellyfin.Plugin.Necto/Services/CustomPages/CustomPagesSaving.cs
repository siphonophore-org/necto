using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.CustomPages;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.CustomPages;

/// <summary>
/// Readies the Pages settings of a configuration about to be saved: every page gets an id, the new store values
/// come into range, the settings are checked, and route passwords are encrypted.
/// </summary>
public static class CustomPagesSaving
{
    /// <summary>The name the route credential keys come from. Changing it makes every stored credential unreadable.</summary>
    public const string SecretsName = "Jellyfin.Plugin.Necto";

    private static readonly object Gate = new();
    private static readonly Dictionary<string, SecretProtector> Protectors = new(StringComparer.Ordinal);

    /// <summary>
    /// Readies the incoming settings for saving.
    /// </summary>
    /// <param name="incoming">The configuration about to be saved. Its Pages settings change in place.</param>
    /// <param name="stored">The configuration in effect, or <c>null</c> on a first save.</param>
    /// <param name="paths">Locates the key folder, or <c>null</c> when it is not known, which keeps passwords in plain text.</param>
    /// <exception cref="ArgumentException">When the settings must not be saved.</exception>
    public static void Apply(PluginConfiguration incoming, PluginConfiguration? stored, IApplicationPaths? paths)
    {
        ArgumentNullException.ThrowIfNull(incoming);

        Apply(incoming, stored, Protector(paths));
    }

    /// <summary>
    /// Readies the incoming settings for saving with a given protector.
    /// </summary>
    /// <param name="incoming">The configuration about to be saved. Its Pages settings change in place.</param>
    /// <param name="stored">The configuration in effect, or <c>null</c> on a first save.</param>
    /// <param name="secrets">Encrypts route passwords.</param>
    /// <exception cref="ArgumentException">When the settings must not be saved.</exception>
    public static void Apply(PluginConfiguration incoming, PluginConfiguration? stored, SecretProtector secrets)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(secrets);

        incoming.CustomPages ??= new CustomPagesSettings();
        Normalize(incoming.CustomPages);
        ConfigurationValidator.Validate(incoming.CustomPages);
        RouteSecrets.Protect(incoming.CustomPages, stored?.CustomPages ?? new CustomPagesSettings(), secrets);
    }

    /// <summary>
    /// Fills in what a save may leave out: the lists, an id for every page, and new store values in range.
    /// </summary>
    /// <param name="settings">The settings to normalize in place.</param>
    public static void Normalize(CustomPagesSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Pages ??= new List<CustomPage>();
        settings.Assets ??= new List<PageAsset>();
        settings.Stores ??= new List<PageStore>();

        foreach (var page in settings.Pages)
        {
            page.ApiRoutes ??= new List<PageApiRoute>();
            if (string.IsNullOrWhiteSpace(page.Id))
            {
                page.Id = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            }
        }

        settings.NewStoreMaxRecords = Math.Clamp(settings.NewStoreMaxRecords, 1, PageStore.RecordCeiling);
        settings.NewStoreRetentionDays = Math.Clamp(settings.NewStoreRetentionDays, 0, PageStore.RetentionDayCeiling);
    }

    /// <summary>
    /// Builds the protector once per key folder, over the same key ring as the one the services use.
    /// </summary>
    private static SecretProtector Protector(IApplicationPaths? paths)
    {
        // With no paths there is no key folder, so the plain text protector is never kept for later saves.
        if (paths is null)
        {
            return StableSecretProtection.CreateProtector(null, SecretsName, NullLogger.Instance);
        }

        lock (Gate)
        {
            var folder = StableSecretProtection.KeyDirectory(paths, SecretsName);
            if (!Protectors.TryGetValue(folder, out var protector))
            {
                protector = StableSecretProtection.CreateProtector(paths, SecretsName, NullLogger.Instance);
                Protectors[folder] = protector;
            }

            return protector;
        }
    }
}
