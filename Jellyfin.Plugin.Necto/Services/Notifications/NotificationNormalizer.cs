using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.Notifications;

namespace Jellyfin.Plugin.Necto.Services.Notifications;

/// <summary>
/// Brings the Notifications settings back to what Necto relies on: the Administrators and Activity log channels exist
/// once each and first, every channel has a unique id, and every list holds no blanks.
/// </summary>
public static class NotificationNormalizer
{
    private static readonly char[] AddressSeparators = { '\n', '\r', ',', ';' };

    /// <summary>
    /// Normalizes the settings in place.
    /// </summary>
    /// <param name="settings">The settings, or <c>null</c> for new ones.</param>
    /// <returns>The settings.</returns>
    public static NotificationSettings Normalize(NotificationSettings? settings)
    {
        settings ??= new NotificationSettings();
        settings.ServerUrl = (settings.ServerUrl ?? string.Empty).Trim().TrimEnd('/');
        settings.PushContact = (settings.PushContact ?? string.Empty).Trim();
        settings.PushPublicKey ??= string.Empty;
        settings.PushPrivateKey ??= string.Empty;
        var smtp = settings.Smtp ??= new SmtpSettings();
        smtp.Host = (smtp.Host ?? string.Empty).Trim();
        smtp.Port = smtp.Port is > 0 and <= 65535 ? smtp.Port : 587;
        smtp.Username = (smtp.Username ?? string.Empty).Trim();
        smtp.Password ??= string.Empty;
        smtp.FromAddress = (smtp.FromAddress ?? string.Empty).Trim();
        smtp.FromName = (smtp.FromName ?? string.Empty).Trim();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<NotificationChannel>();
        foreach (var channel in (settings.Channels ?? new List<NotificationChannel>()).Where(c => c is not null))
        {
            if (!Enum.IsDefined(channel.Kind) || channel.IsBuiltIn)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(channel.Id) || !seen.Add(channel.Id) || channel.Id is NotificationChannel.AdministratorsId or NotificationChannel.ActivityLogId)
            {
                channel.Id = Guid.NewGuid().ToString("N");
                seen.Add(channel.Id);
            }

            Clean(channel);
            kept.Add(channel);
        }

        var stored = settings.Channels ?? new List<NotificationChannel>();
        var builtIns = new[]
        {
            BuiltIn(stored, NotificationChannelKind.Administrators, NotificationChannel.AdministratorsId, allEvents: true),
            BuiltIn(stored, NotificationChannelKind.ActivityLog, NotificationChannel.ActivityLogId, allEvents: false)
        };
        settings.Channels = builtIns.Concat(kept).ToList();
        return settings;
    }

    /// <summary>
    /// Splits an email channel's addresses into a list, one a line or split by commas or semicolons.
    /// </summary>
    /// <param name="recipients">The addresses as written.</param>
    /// <returns>Each address once, trimmed.</returns>
    public static IReadOnlyList<string> Addresses(string? recipients)
        => (recipients ?? string.Empty)
            .Split(AddressSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static NotificationChannel BuiltIn(List<NotificationChannel> stored, NotificationChannelKind kind, string id, bool allEvents)
    {
        var channel = stored.FirstOrDefault(c => c is not null && c.Kind == kind)
            ?? new NotificationChannel { Kind = kind, AllEvents = allEvents };
        channel.Id = id;
        channel.Name = string.Empty;
        channel.Url = string.Empty;
        channel.Headers = string.Empty;
        channel.Recipients = string.Empty;
        channel.UseCustomBody = false;
        channel.Body = string.Empty;
        Clean(channel);
        return channel;
    }

    private static void Clean(NotificationChannel channel)
    {
        channel.Kind = channel.Kind;
        channel.Name = (channel.Name ?? string.Empty).Trim();
        channel.Url ??= string.Empty;
        channel.Headers ??= string.Empty;
        channel.Body ??= string.Empty;
        channel.Username = (channel.Username ?? string.Empty).Trim();
        channel.AvatarUrl = (channel.AvatarUrl ?? string.Empty).Trim();
        channel.Recipients = string.Join("\n", Addresses(channel.Recipients));
        channel.EventKeys = (channel.EventKeys ?? new List<string>())
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
