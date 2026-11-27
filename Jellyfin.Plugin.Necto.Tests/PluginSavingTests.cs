using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Invites;
using Jellyfin.Plugin.Necto.Configuration.Notifications;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.WireGuard;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
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

    /// <summary>
    /// A page that reads the whole configuration and saves it back, with every secret as the kept marker, keeps every
    /// stored secret.
    /// True: saving any page leaves passwords, keys, and PINs as they were.
    /// False: a save from Settings or the dashboard would wipe every secret it never showed.
    /// </summary>
    [Fact]
    public void UpdateConfiguration_KeptMarkers_KeepEveryStoredSecret()
    {
        var plugin = TestPlugin.CreateWithSettings();
        var invite = System.Guid.NewGuid();
        var server = System.Guid.NewGuid().ToString("N");
        plugin.MutateConfiguration(c =>
        {
            c.WireGuard.HostPrivateKey = "enc:v1:host";
            c.WireGuard.Peers.Add(new TunnelPeer { Name = "Phone", Address = "10.66.0.2", PublicKey = "pub", PrivateKey = "enc:v1:peer", PresharedKey = "enc:v1:psk" });
            c.Ddns.Records.Add(new DNSRecord { Hostname = "media.example.com", Login = "enc:v1:login", Password = "enc:v1:password" });
            c.Proxy.Routes.Add(new ProxyRoute { Host = "media.example.com", Certificate = new ProxyCertificateSettings { Password = "enc:v1:certificate" } });
            c.Requests.TmdbApiKey = "enc:v1:tmdb";
            c.Requests.ArrServers.Add(new ArrServer { Id = server, Type = RequestType.Movie, Name = "Radarr", Url = "http://localhost:7878", ApiKey = "enc:v1:radarr" });
            c.Invites.Add(new Invite { Id = invite, Token = "token", PinHash = "$PBKDF2$pin" });
            return false;
        });
        var page = System.Text.Json.JsonSerializer.Deserialize<PluginConfiguration>(System.Text.Json.JsonSerializer.Serialize(plugin.Configuration))!;
        Assert.Equal(Siphonophore.Stolon.SecretProtector.KeptSentinel, page.Requests.TmdbApiKey);

        plugin.UpdateConfiguration(page);

        var saved = plugin.Configuration;
        Assert.Equal("enc:v1:host", saved.WireGuard.HostPrivateKey);
        Assert.Equal("enc:v1:psk", saved.WireGuard.Peers[0].PresharedKey);
        Assert.Equal("enc:v1:password", saved.Ddns.Records[0].Password);
        Assert.Equal("enc:v1:certificate", saved.Proxy.Routes[0].Certificate.Password);
        Assert.Equal("enc:v1:tmdb", saved.Requests.TmdbApiKey);
        Assert.Equal("enc:v1:radarr", saved.Requests.ArrServers[0].ApiKey);
        Assert.Equal("$PBKDF2$pin", saved.Invites[0].PinHash);
    }
}
