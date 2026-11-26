using System;
using Jellyfin.Plugin.Necto.Configuration.Scripts;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Scripts;

/// <summary>
/// Prepares the Scripts part of a configuration save. It fills in what a page may leave out, refuses scripts that
/// could not run, and moves each secret value into the secret vault, so the configuration keeps none.
/// </summary>
public static class ScriptSettingsSaving
{
    /// <summary>The name the secret protector is derived from. Changing it makes every stored secret unreadable.</summary>
    public const string SecretsName = "Jellyfin.Plugin.Necto";

    /// <summary>
    /// Prepares the incoming Scripts settings.
    /// </summary>
    /// <param name="incoming">The settings about to be saved, or null.</param>
    /// <param name="stored">The settings in effect.</param>
    /// <param name="paths">Gives the vault's folder and the key folder, or null to leave the vault alone, as in tests.</param>
    /// <returns>The settings to save.</returns>
    /// <exception cref="ArgumentException">When a setting or a script is not valid.</exception>
    public static ScriptSettings Saving(ScriptSettings? incoming, ScriptSettings? stored, IApplicationPaths? paths)
    {
        var result = incoming ?? new ScriptSettings();
        stored ??= new ScriptSettings();
        ConfigurationValidator.Normalize(result);
        ConfigurationValidator.Validate(result);
        if (paths is not null)
        {
            var vault = new SecretVault(SecretVault.FileIn(paths), NullLogger.Instance);
            var protector = StableSecretProtection.CreateProtector(paths, SecretsName, NullLogger.Instance);
            vault.Save(ScriptSecrets.Store(result, stored, vault.Load(), protector));
        }

        return result;
    }
}
