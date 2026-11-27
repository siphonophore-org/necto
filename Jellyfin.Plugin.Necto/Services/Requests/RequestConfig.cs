using System;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Requests;

/// <summary>
/// Reads and changes the Requests settings of the loaded plugin, under the plugin's configuration lock.
/// </summary>
public sealed class RequestConfig
{
    private static readonly RequestConfig Shared = new();

    private RequestConfig()
    {
    }

    /// <summary>Gets the settings of the loaded plugin, or <c>null</c> before it loads.</summary>
    public static RequestConfig? Instance => Plugin.Instance is null ? null : Shared;

    /// <summary>Gets the protector the Radarr, Sonarr, and TMDb keys are stored with.</summary>
    public SecretProtector Secrets => RequestSecrets.Current;

    /// <summary>
    /// Reads from the Requests settings.
    /// </summary>
    /// <typeparam name="T">What is read.</typeparam>
    /// <param name="read">Picks the value.</param>
    /// <returns>The value.</returns>
    public T ReadConfiguration<T>(Func<RequestSettings, T> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        return Loaded().ReadConfiguration(c => read(c.Requests ??= new RequestSettings()));
    }

    /// <summary>
    /// Reads from the Issues settings.
    /// </summary>
    /// <typeparam name="T">What is read.</typeparam>
    /// <param name="read">Picks the value.</param>
    /// <returns>The value.</returns>
    public T ReadIssues<T>(Func<IssueSettings, T> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        return Loaded().ReadConfiguration(c => read(c.Issues ??= new IssueSettings()));
    }

    /// <summary>
    /// Reads from the whole configuration, for what Requests needs beside its own settings, such as the groups.
    /// </summary>
    /// <typeparam name="T">What is read.</typeparam>
    /// <param name="read">Picks the value.</param>
    /// <returns>The value.</returns>
    public T ReadAll<T>(Func<PluginConfiguration, T> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        return Loaded().ReadConfiguration(read);
    }

    /// <summary>
    /// Changes the Requests settings, and saves them when the change asks.
    /// </summary>
    /// <param name="mutate">Changes the settings, and returns <c>true</c> to save them.</param>
    public void MutateConfiguration(Func<RequestSettings, bool> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        Loaded().MutateConfiguration(c => mutate(c.Requests ??= new RequestSettings()));
    }

    /// <summary>
    /// Changes the whole configuration, and saves it when the change asks.
    /// </summary>
    /// <param name="mutate">Changes the configuration, and returns <c>true</c> to save it.</param>
    public void MutateAll(Func<PluginConfiguration, bool> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        Loaded().MutateConfiguration(mutate);
    }

    private static Plugin Loaded() => Plugin.Instance ?? throw new InvalidOperationException("The plugin is not loaded.");
}
