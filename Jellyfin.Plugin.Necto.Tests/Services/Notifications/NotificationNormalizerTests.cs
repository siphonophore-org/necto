using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.Notifications;
using Jellyfin.Plugin.Necto.Services.Notifications;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Notifications;

/// <summary>
/// The settings always hold the Administrators and Activity log channels once each and first, and no channel shares
/// an id.
/// </summary>
public class NotificationNormalizerTests
{
    /// <summary>
    /// New settings get the two built in channels, Administrators taking every event and the activity log none.
    /// True: a fresh install sends administrators every event on their apps and writes nothing twice to the log.
    /// False: a fresh install would have nowhere to send events.
    /// </summary>
    [Fact]
    public void Normalize_NewSettings_AddsTheBuiltInChannels()
    {
        var settings = NotificationNormalizer.Normalize(null);

        Assert.Equal(new[] { NotificationChannel.AdministratorsId, NotificationChannel.ActivityLogId }, settings.Channels.Select(c => c.Id));
        Assert.True(settings.Channels[0].AllEvents);
        Assert.False(settings.Channels[1].AllEvents);
        Assert.Empty(settings.Channels[1].EventKeys);
    }

    /// <summary>
    /// A built in channel keeps its events, and a duplicate of it is dropped.
    /// True: an administrator's picks on a built in channel survive a save.
    /// False: the built in channels would reset or appear twice.
    /// </summary>
    [Fact]
    public void Normalize_KeepsBuiltInEvents_AndDropsDuplicates()
    {
        var settings = new NotificationSettings();
        settings.Channels.Add(new NotificationChannel { Kind = NotificationChannelKind.ActivityLog, EventKeys = { "invites.used" } });
        settings.Channels.Add(new NotificationChannel { Kind = NotificationChannelKind.ActivityLog, EventKeys = { "x.y" } });

        NotificationNormalizer.Normalize(settings);

        Assert.Single(settings.Channels, c => c.Kind == NotificationChannelKind.ActivityLog);
        Assert.Equal(new[] { "invites.used" }, settings.Channels[1].EventKeys);
    }

    /// <summary>
    /// Added channels get unique ids and clean lists, and their kind sets the Stolon channel.
    /// True: each channel's test and secrets name one channel.
    /// False: two channels could share stored secrets.
    /// </summary>
    [Fact]
    public void Normalize_AddedChannels_GetUniqueIdsAndCleanLists()
    {
        var settings = new NotificationSettings();
        settings.Channels.Add(new NotificationChannel { Id = "a", Name = " Hook ", Kind = NotificationChannelKind.Webhook, EventKeys = { " invites.used ", "", "invites.used" } });
        settings.Channels.Add(new NotificationChannel { Id = "a", Name = "Mail", Kind = NotificationChannelKind.Email, Recipients = "a@example.com, b@example.com;\na@example.com" });

        NotificationNormalizer.Normalize(settings);

        var added = settings.Channels.Skip(2).ToList();
        Assert.Equal(2, added.Select(c => c.Id).Distinct().Count());
        Assert.Equal("Hook", added[0].Name);
        Assert.Equal(new[] { "invites.used" }, added[0].EventKeys);
        Assert.Equal(Siphonophore.Stolon.NotifyChannel.Webhook, added[0].Channel);
        Assert.Equal("a@example.com\nb@example.com", added[1].Recipients);
    }

    /// <summary>
    /// A bad port falls back to 587, and the server address loses its trailing slash.
    /// True: a typo never leaves email unable to connect, and links never double their slash.
    /// False: a port of 0 would break every email.
    /// </summary>
    [Fact]
    public void Normalize_CleansTheServerAndSmtpFields()
    {
        var settings = NotificationNormalizer.Normalize(new NotificationSettings { ServerUrl = "https://media.example.com/ ", Smtp = new SmtpSettings { Port = 0, Host = " smtp.example.com " } });

        Assert.Equal("https://media.example.com", settings.ServerUrl);
        Assert.Equal(587, settings.Smtp.Port);
        Assert.Equal("smtp.example.com", settings.Smtp.Host);
    }
}
