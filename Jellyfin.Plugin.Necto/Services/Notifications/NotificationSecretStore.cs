using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.Necto.Configuration.Notifications;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Notifications;

/// <summary>
/// Keeps the Notifications secrets in an encrypted file beside the configuration, readable by the server's account
/// alone: the SMTP password, the VAPID private key, and each channel's address, headers, and email addresses. The
/// configuration holds none of them.
/// </summary>
[PluginService(ServiceLifetime.Singleton)]
public sealed class NotificationSecretStore
{
    /// <summary>The file the secrets are kept in, in the plugin configurations folder.</summary>
    public const string FileName = "Jellyfin.Plugin.Necto.Notifications.Secrets.json";

    private readonly string _path;
    private readonly SecretProtector _secrets;
    private readonly ILogger? _logger;
    private readonly object _lock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationSecretStore"/> class.
    /// </summary>
    /// <param name="paths">Gives the folder the secrets are kept in.</param>
    /// <param name="secrets">Encrypts the email addresses.</param>
    /// <param name="logger">Writes the server log.</param>
    public NotificationSecretStore(IApplicationPaths paths, SecretProtector secrets, ILogger<NotificationSecretStore> logger)
        : this(Path.Join((paths ?? throw new ArgumentNullException(nameof(paths))).PluginConfigurationsPath, FileName), secrets, logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationSecretStore"/> class at a path.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="secrets">Encrypts the email addresses.</param>
    /// <param name="logger">Writes the server log, or <c>null</c>.</param>
    internal NotificationSecretStore(string path, SecretProtector secrets, ILogger? logger)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _path = path;
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        _logger = logger;
    }

    /// <summary>
    /// Reads the stored secrets.
    /// </summary>
    /// <returns>The secrets, or none when the file is missing or cannot be read.</returns>
    public NotificationSecrets Load()
    {
        lock (_lock)
        {
            return Read();
        }
    }

    /// <summary>
    /// Puts the stored secrets into a copy of the settings: the encrypted password, private key, addresses, and
    /// headers, and the plain email addresses.
    /// </summary>
    /// <param name="settings">A copy of the settings, changed in place. Never the stored configuration.</param>
    /// <returns>The settings.</returns>
    public NotificationSettings Fill(NotificationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var stored = Load();
        settings.Smtp ??= new SmtpSettings();
        settings.Smtp.Password = stored.SmtpPassword;
        settings.PushPrivateKey = stored.PushPrivateKey;
        foreach (var channel in settings.Channels ?? Enumerable.Empty<NotificationChannel>())
        {
            if (channel is null)
            {
                continue;
            }

            var own = channel.Id is { } id && stored.Channels.TryGetValue(id, out var found) ? found : new NotificationChannelSecrets();
            channel.Url = own.Url;
            channel.Headers = own.Headers;
            channel.Recipients = _secrets.Unprotect(own.Recipients);
        }

        return settings;
    }

    /// <summary>
    /// Moves the secrets of settings about to be stored into the file and clears them from the settings. A channel
    /// the settings no longer have loses its secrets. The VAPID private key stays as stored.
    /// </summary>
    /// <param name="settings">The settings, with each secret resolved to the value to keep, changed in place.</param>
    public void Keep(NotificationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_lock)
        {
            var next = new NotificationSecrets { PushPrivateKey = Read().PushPrivateKey };
            settings.Smtp ??= new SmtpSettings();
            next.SmtpPassword = _secrets.Protect(settings.Smtp.Password);
            settings.Smtp.Password = string.Empty;
            settings.PushPrivateKey = string.Empty;
            foreach (var channel in (settings.Channels ?? Enumerable.Empty<NotificationChannel>()).Where(c => c is not null))
            {
                var own = new NotificationChannelSecrets
                {
                    Url = _secrets.Protect(channel.Url),
                    Headers = _secrets.Protect(channel.Headers),
                    Recipients = _secrets.Protect(channel.Recipients)
                };
                channel.Url = string.Empty;
                channel.Headers = string.Empty;
                channel.Recipients = string.Empty;
                if (!string.IsNullOrEmpty(channel.Id) && (own.Url.Length > 0 || own.Headers.Length > 0 || own.Recipients.Length > 0))
                {
                    next.Channels[channel.Id] = own;
                }
            }

            Write(next);
        }
    }

    /// <summary>
    /// Keeps a new VAPID private key.
    /// </summary>
    /// <param name="protectedKey">The encrypted key.</param>
    public void SetPushPrivateKey(string protectedKey)
    {
        lock (_lock)
        {
            var stored = Read();
            stored.PushPrivateKey = protectedKey ?? string.Empty;
            Write(stored);
        }
    }

    // Called under _lock.
    private NotificationSecrets Read()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new NotificationSecrets();
            }

            var read = JsonSerializer.Deserialize<NotificationSecrets>(File.ReadAllBytes(_path)) ?? new NotificationSecrets();
            read.Channels = new(read.Channels ?? new(), StringComparer.Ordinal);
            return read;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Could not read the Notifications secrets, so none are used");
            return new NotificationSecrets();
        }
    }

    // Called under _lock.
    private void Write(NotificationSecrets secrets)
    {
        var folder = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        AtomicFile.WriteAllBytes(_path, JsonSerializer.SerializeToUtf8Bytes(secrets), ownerOnly: true, logger: _logger);
    }
}
