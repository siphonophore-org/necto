using System;
using System.Linq;
using System.Net.Mail;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Notifications;
using Jellyfin.Plugin.Necto.Services.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Notifications;

/// <summary>
/// Reads and saves the Notifications settings for the Settings section. The secrets live in the
/// <see cref="NotificationSecretStore"/>, never the configuration. A copy sent to a page carries each stored address,
/// header, and password only as kept, and a save keeps each one the page sent back as kept.
/// </summary>
[PluginService(ServiceLifetime.Singleton)]
public sealed class NotificationSettingsService
{
    private readonly IConfigurationStore _store;
    private readonly SecretProtector _secrets;
    private readonly NotificationSecretStore _kept;

    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationSettingsService"/> class.
    /// </summary>
    /// <param name="store">Reads and saves the configuration.</param>
    /// <param name="secrets">Encrypts the addresses, headers, and password.</param>
    /// <param name="kept">Keeps the secrets out of the configuration.</param>
    public NotificationSettingsService(IConfigurationStore store, SecretProtector secrets, NotificationSecretStore kept)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        _kept = kept ?? throw new ArgumentNullException(nameof(kept));
    }

    /// <summary>
    /// Reads the settings with their secrets hidden.
    /// </summary>
    /// <returns>A copy for a page.</returns>
    public NotificationSettings Read()
    {
        var copy = _store.Read(c => NotificationNormalizer.Normalize(ConfigurationCopy.Of(c.Notifications ?? new NotificationSettings())));
        return Hide(_kept.Fill(copy));
    }

    /// <summary>
    /// Hides every secret in a copy of the settings.
    /// </summary>
    /// <param name="copy">The copy, changed in place.</param>
    /// <returns>The copy.</returns>
    public static NotificationSettings Hide(NotificationSettings copy)
    {
        ArgumentNullException.ThrowIfNull(copy);
        foreach (var channel in copy.Channels)
        {
            channel.HideSecrets();
        }

        copy.Smtp.Password = SecretProtector.HasSecret(copy.Smtp.Password) ? SecretProtector.KeptSentinel : string.Empty;
        copy.PushPrivateKey = string.Empty;
        return copy;
    }

    /// <summary>
    /// Checks and saves the settings a page sent, the secrets to the <see cref="NotificationSecretStore"/>. The VAPID
    /// keys are never taken from a page.
    /// </summary>
    /// <param name="incoming">The settings as the page sent them.</param>
    /// <param name="language">The language of a refusal, or <c>null</c> for US English.</param>
    /// <returns>Why the settings were refused, or <c>null</c> when they were saved.</returns>
    public string? Save(NotificationSettings incoming, string? language)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        var settings = NotificationNormalizer.Normalize(ConfigurationCopy.Of(incoming));
        var error = Check(settings, language);
        if (error is not null)
        {
            return error;
        }

        _store.Mutate(config =>
        {
            var stored = _kept.Fill(ConfigurationCopy.Of(config.Notifications ?? new NotificationSettings()));
            foreach (var channel in settings.Channels)
            {
                var old = stored.Channels?.FirstOrDefault(c => c is not null && string.Equals(c.Id, channel.Id, StringComparison.Ordinal));
                NotifyDestination.ResolveSecrets(channel, old is not null && old.Kind == channel.Kind ? old : null, _secrets);
            }

            var sameServer = string.Equals(settings.Smtp.Host, stored.Smtp?.Host, StringComparison.OrdinalIgnoreCase);
            settings.Smtp.Password = _secrets.ResolveIncoming(
                SecretProtector.IsProtected(settings.Smtp.Password) ? string.Empty : settings.Smtp.Password,
                sameServer ? stored.Smtp?.Password : null);
            settings.PushPublicKey = stored.PushPublicKey ?? string.Empty;
            _kept.Keep(settings);
            config.Notifications = settings;
            return true;
        });
        return null;
    }

    /// <summary>
    /// Finds the first thing wrong with settings a page sent.
    /// </summary>
    /// <param name="settings">The normalized settings.</param>
    /// <param name="language">The language of the reason.</param>
    /// <returns>The reason, or <c>null</c> when nothing is wrong.</returns>
    public static string? Check(NotificationSettings settings, string? language)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.ServerUrl.Length > 0 && !IsWebAddress(settings.ServerUrl))
        {
            return StolonTranslations.Get(TranslationKeys.NotificationsErrorServerUrl, language);
        }

        if (settings.PushContact.Length > 0 && WebPushKeys.ContactFor(settings.PushContact) == WebPushKeys.DefaultContact
            && !string.Equals(settings.PushContact, WebPushKeys.DefaultContact, StringComparison.OrdinalIgnoreCase))
        {
            return StolonTranslations.Get(TranslationKeys.NotificationsErrorPushContact, language);
        }

        if (settings.Smtp.FromAddress.Length > 0 && !IsEmail(settings.Smtp.FromAddress))
        {
            return StolonTranslations.Get(TranslationKeys.NotificationsErrorFromAddress, language);
        }

        foreach (var channel in settings.Channels.Where(c => !c.IsBuiltIn))
        {
            var name = channel.Name.Length > 0 ? channel.Name : channel.Kind.ToString();
            if (channel.Name.Length == 0)
            {
                return StolonTranslations.Get(TranslationKeys.NotificationsErrorChannelName, language);
            }

            if (channel.Kind is NotificationChannelKind.Discord or NotificationChannelKind.Webhook
                && channel.Url.Length > 0 && channel.Url != SecretProtector.KeptSentinel && !SecretProtector.IsProtected(channel.Url) && !IsWebAddress(channel.Url))
            {
                return StolonTranslations.Format(TranslationKeys.NotificationsErrorChannelUrl, language, name);
            }

            if (channel.Kind == NotificationChannelKind.Discord && channel.AvatarUrl.Length > 0 && !IsWebAddress(channel.AvatarUrl))
            {
                return StolonTranslations.Format(TranslationKeys.NotificationsErrorAvatarUrl, language, name);
            }

            if (channel.Kind == NotificationChannelKind.Email && NotificationNormalizer.Addresses(channel.Recipients).FirstOrDefault(a => !IsEmail(a)) is { } bad)
            {
                return StolonTranslations.Format(TranslationKeys.NotificationsErrorRecipient, language, bad);
            }
        }

        return null;
    }

    private static bool IsWebAddress(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) && string.IsNullOrEmpty(uri.UserInfo);

    private static bool IsEmail(string value) => MailAddress.TryCreate(value, out var parsed) && string.Equals(parsed.Address, value, StringComparison.OrdinalIgnoreCase);
}
