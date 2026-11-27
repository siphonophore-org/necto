using System;
using System.Collections.Generic;
using System.Text.Json;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.CustomPages;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Invites;
using Jellyfin.Plugin.Necto.Configuration.Notifications;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Scripts;
using Jellyfin.Plugin.Necto.Configuration.WireGuard;
using Jellyfin.Plugin.Necto.Services.Notifications;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Siphonophore.Stolon;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Configuration;

/// <summary>
/// No JSON the server sends carries a secret setting: Jellyfin's configuration read gets the kept marker, while a copy
/// kept on the server keeps every value.
/// </summary>
public sealed class KeptSecretConverterTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly SecretProtector _secrets = TestSecrets.Create();

    public void Dispose() => _folder.Dispose();

    /// <summary>
    /// Builds a configuration holding every kind of secret Necto keeps, with the Notifications ones saved through its
    /// own route, and lists each secret's stored value and plain value.
    /// </summary>
    private (PluginConfiguration Config, List<string> Secrets) EverySecret()
    {
        var secrets = new List<string>();
        string Secret(string plain)
        {
            var stored = _secrets.Protect(plain);
            secrets.Add(plain);
            secrets.Add(stored);
            return stored;
        }

        var config = new PluginConfiguration();
        config.WireGuard.HostPrivateKey = Secret("wireguard-host-private");
        config.WireGuard.Peers.Add(new TunnelPeer { Name = "Phone", PrivateKey = Secret("wireguard-peer-private"), PresharedKey = Secret("wireguard-peer-preshared") });
        config.WireGuard.Connections.Add(new TunnelConnection { PrivateKey = Secret("wireguard-connection-private"), PresharedKey = Secret("wireguard-connection-preshared") });
        config.Ddns.Records.Add(new DNSRecord { Hostname = "media.example.com", Login = Secret("ddns-login"), Password = Secret("ddns-password") });
        config.Proxy.Routes.Add(new ProxyRoute { Host = "media.example.com", Certificate = new ProxyCertificateSettings { Password = Secret("proxy-certificate-password") } });
        config.Requests.TmdbApiKey = Secret("tmdb-api-key");
        config.Requests.ArrServers.Add(new ArrServer { Url = "http://localhost:7878", ApiKey = Secret("radarr-api-key") });
        config.CustomPages.Pages.Add(new CustomPage { Slug = "status", ApiRoutes = { new PageApiRoute { Name = "api", Url = "https://api.example.com", Password = Secret("route-password") } } });
        config.Scripts.Scripts.Add(new ScriptDefinition { Secrets = { new ScriptSecret { Name = "TOKEN", Value = Secret("script-secret") } } });
        config.Invites.Add(new Invite { Id = Guid.NewGuid(), Token = "token", PinHash = Secret("$PBKDF2-SHA512$iterations=1$pin-hash") });

        var store = new FakeConfigurationStore(config);
        var service = new NotificationSettingsService(store, _secrets, new NotificationSecretStore(_folder.File("secrets.json"), _secrets, null));
        var notifications = new NotificationSettings
        {
            Smtp = new SmtpSettings { Host = "smtp.example.com", FromAddress = "jellyfin@example.com", Password = "smtp-password" },
            Channels =
            {
                new NotificationChannel { Id = "hook", Name = "Hook", Kind = NotificationChannelKind.Webhook, Url = "https://hooks.example.com/webhook-token", Headers = "Authorization: webhook-header" },
                new NotificationChannel { Id = "discord", Name = "Discord", Kind = NotificationChannelKind.Discord, Url = "https://discord.com/api/webhooks/1/discord-token" },
                new NotificationChannel { Id = "mail", Name = "Mail", Kind = NotificationChannelKind.Email, Recipients = "admin@example.com" }
            }
        };
        Assert.Null(service.Save(notifications, null));
        config.Notifications.PushPrivateKey = Secret("vapid-private-key");
        secrets.AddRange(new[] { "smtp-password", "webhook-token", "webhook-header", "discord-token", "admin@example.com" });
        return (config, secrets);
    }

    /// <summary>
    /// A configuration holding every kind of secret serializes with none of them, plain or encrypted.
    /// True: Jellyfin's GET /Plugins/{id}/Configuration hands a page no secret.
    /// False: any page, or anyone with an administrator's token, could read every password, key, and token.
    /// </summary>
    /// <param name="camelCase">Whether the JSON is camel case, as some clients ask.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Serialize_EveryKindOfSecret_NoneAppears(bool camelCase)
    {
        var (config, secrets) = EverySecret();
        var options = new JsonSerializerOptions { PropertyNamingPolicy = camelCase ? JsonNamingPolicy.CamelCase : null };

        var json = JsonSerializer.Serialize<object>(config, options);

        Assert.All(secrets, secret => Assert.DoesNotContain(secret, json, StringComparison.Ordinal));
        Assert.Contains(SecretProtector.KeptSentinel, json, StringComparison.Ordinal);
    }

    /// <summary>
    /// A copy kept on the server keeps every secret.
    /// True: the tools that read a copy still sign in, encrypt, and sign with the stored values.
    /// False: a copy would hold only the marker, and every provider, tunnel, and server would fail to authenticate.
    /// </summary>
    [Fact]
    public void ConfigurationCopy_KeepsEverySecret()
    {
        var (config, _) = EverySecret();

        var copy = ConfigurationCopy.Of(config);

        Assert.Equal(config.WireGuard.HostPrivateKey, copy.WireGuard.HostPrivateKey);
        Assert.Equal(config.WireGuard.Peers[0].PresharedKey, copy.WireGuard.Peers[0].PresharedKey);
        Assert.Equal(config.Ddns.Records[0].Password, copy.Ddns.Records[0].Password);
        Assert.Equal(config.Proxy.Routes[0].Certificate.Password, copy.Proxy.Routes[0].Certificate.Password);
        Assert.Equal(config.Requests.ArrServers[0].ApiKey, copy.Requests.ArrServers[0].ApiKey);
        Assert.Equal(config.CustomPages.Pages[0].ApiRoutes[0].Password, copy.CustomPages.Pages[0].ApiRoutes[0].Password);
        Assert.Equal(config.Invites[0].PinHash, copy.Invites[0].PinHash);
        Assert.Equal(config.Notifications.PushPrivateKey, copy.Notifications.PushPrivateKey);
    }

    /// <summary>
    /// A secret read in is taken as sent, so the marker a page sends back reaches the save as the marker.
    /// True: a save resolves the marker to the stored value.
    /// False: a page's save could not say a secret was left alone.
    /// </summary>
    [Fact]
    public void Deserialize_TakesTheValueAsSent()
    {
        var json = "{\"ApiKey\":\"" + SecretProtector.KeptSentinel + "\",\"Url\":\"http://localhost:8989\"}";

        var server = JsonSerializer.Deserialize<ArrServer>(json);

        Assert.Equal(SecretProtector.KeptSentinel, server!.ApiKey);
    }

    /// <summary>
    /// An unset secret is written empty, not as the marker.
    /// True: a page shows an empty field for a secret never set.
    /// False: every page would show a saved secret that does not exist.
    /// </summary>
    [Fact]
    public void Serialize_UnsetSecret_IsEmpty()
    {
        var json = JsonSerializer.Serialize(new ArrServer { ApiKey = string.Empty });

        Assert.Contains("\"ApiKey\":\"\"", json, StringComparison.Ordinal);
    }
}
