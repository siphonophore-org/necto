using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Notifications;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests;

/// <summary>
/// A save of the whole configuration, as the dashboard and Settings make, keeps the parts that change only through
/// their own routes. It loads the plugin, so it runs on its own.
/// </summary>
[Collection(ServicesCollection.Name)]
public class PluginSavingTests
{
    /// <summary>
    /// A whole save keeps the stored Notifications settings, whatever it sends.
    /// True: only the Notifications route, which keeps every secret, changes channels, the SMTP password, and push keys.
    /// False: a Settings page that read the whole configuration could write back channels and secrets it held.
    /// </summary>
    [Fact]
    public void UpdateConfiguration_KeepsTheStoredNotifications()
    {
        var plugin = TestPlugin.CreateWithSettings();
        plugin.MutateConfiguration(c =>
        {
            c.Notifications.ServerUrl = "https://media.example.com";
            c.Notifications.Smtp.Password = "enc:v1:secret";
            return false;
        });
        var incoming = new PluginConfiguration();
        incoming.Notifications.ServerUrl = "https://evil.example.com";
        incoming.Notifications.Channels.Add(new NotificationChannel { Kind = NotificationChannelKind.Webhook, Url = "https://evil.example.com/hook" });

        plugin.UpdateConfiguration(incoming);

        Assert.Equal("https://media.example.com", plugin.Configuration.Notifications.ServerUrl);
        Assert.Equal("enc:v1:secret", plugin.Configuration.Notifications.Smtp.Password);
        Assert.Empty(plugin.Configuration.Notifications.Channels);
    }
}
