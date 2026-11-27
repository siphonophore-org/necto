using System;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.Notifications;
using Jellyfin.Plugin.Necto.Services.Notifications;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Siphonophore.Stolon;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Notifications;

/// <summary>
/// The settings reach a page with every secret hidden, the secrets live in their own file and never the
/// configuration, a save keeps each secret sent back as kept, and settings that would not work are refused with a reason.
/// </summary>
public sealed class NotificationSettingsServiceTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly FakeConfigurationStore _config = new();
    private readonly SecretProtector _secrets = TestSecrets.Create();

    public void Dispose() => _folder.Dispose();

    private NotificationSecretStore Kept() => new(_folder.File("secrets.json"), _secrets, null);

    private NotificationSettingsService Service() => new(_config, _secrets, Kept());

    private static NotificationSettings WithHook(string url, string headers = "") => new()
    {
        Channels = { new NotificationChannel { Id = "hook", Name = "Hook", Kind = NotificationChannelKind.Webhook, Url = url, Headers = headers } },
        Smtp = new SmtpSettings { Host = "smtp.example.com", FromAddress = "jellyfin@example.com", Password = "hunter2" }
    };

    /// <summary>
    /// A saved address, header, password, and email address go encrypted to the secrets file, never the configuration,
    /// and read back hidden.
    /// True: no secret ever reaches the browser or sits in the configuration Jellyfin hands to a page.
    /// False: a webhook token, SMTP password, or the administrators' addresses would leak.
    /// </summary>
    [Fact]
    public void Save_MovesSecretsToTheirFile_AndReadHidesThem()
    {
        var settings = WithHook("https://hooks.example.com/x", "Authorization: token");
        settings.Channels.Add(new NotificationChannel { Id = "mail", Name = "Mail", Kind = NotificationChannelKind.Email, Recipients = "admin@example.com" });
        Assert.Null(Service().Save(settings, null));

        var config = _config.Configuration.Notifications;
        Assert.All(config.Channels, c => Assert.True(c.Url.Length == 0 && c.Headers.Length == 0 && c.Recipients.Length == 0));
        Assert.Empty(config.Smtp.Password);
        var file = Kept().Load();
        Assert.True(SecretProtector.IsProtected(file.Channels["hook"].Url));
        Assert.Equal("https://hooks.example.com/x", _secrets.Unprotect(file.Channels["hook"].Url));
        Assert.Equal("hunter2", _secrets.Unprotect(file.SmtpPassword));
        Assert.Equal("admin@example.com", _secrets.Unprotect(file.Channels["mail"].Recipients));

        var read = Service().Read();
        Assert.Equal(SecretProtector.KeptSentinel, read.Channels.Single(c => c.Id == "hook").Url);
        Assert.Equal(SecretProtector.KeptSentinel, read.Smtp.Password);
        Assert.Empty(read.PushPrivateKey);
        Assert.Equal("admin@example.com", read.Channels.Single(c => c.Id == "mail").Recipients);
    }

    /// <summary>
    /// A channel that is removed takes its secrets with it.
    /// True: a removed webhook's token is not kept on the server.
    /// False: secrets of channels long gone would pile up in the file.
    /// </summary>
    [Fact]
    public void Save_RemovedChannel_DropsItsSecrets()
    {
        Service().Save(WithHook("https://hooks.example.com/x"), null);
        var again = Service().Read();
        again.Channels.RemoveAll(c => c.Id == "hook");

        Assert.Null(Service().Save(again, null));

        Assert.Empty(Kept().Load().Channels);
    }

    /// <summary>
    /// A secret sent back as kept stays as stored.
    /// True: saving other settings never wipes a webhook's address or the SMTP password.
    /// False: every save from the page would clear the secrets.
    /// </summary>
    [Fact]
    public void Save_KeptSecrets_StayAsStored()
    {
        Service().Save(WithHook("https://hooks.example.com/x", "Authorization: token"), null);
        var again = Service().Read();
        again.ServerUrl = "https://media.example.com";

        Assert.Null(Service().Save(again, null));

        var file = Kept().Load();
        Assert.Equal("https://hooks.example.com/x", _secrets.Unprotect(file.Channels["hook"].Url));
        Assert.Equal("Authorization: token", _secrets.Unprotect(file.Channels["hook"].Headers));
        Assert.Equal("hunter2", _secrets.Unprotect(file.SmtpPassword));
    }

    /// <summary>
    /// The VAPID keys are never taken from a page.
    /// True: a page cannot replace the server's push keys.
    /// False: a post could swap the keys and break every browser's subscription.
    /// </summary>
    [Fact]
    public void Save_KeepsTheStoredPushKeys()
    {
        _config.Configuration.Notifications.PushPublicKey = "public";
        Kept().SetPushPrivateKey("private");

        Service().Save(new NotificationSettings { PushPublicKey = "other", PushPrivateKey = "other" }, null);

        Assert.Equal("public", _config.Configuration.Notifications.PushPublicKey);
        Assert.Empty(_config.Configuration.Notifications.PushPrivateKey);
        Assert.Equal("private", Kept().Load().PushPrivateKey);
    }

    /// <summary>
    /// Settings that cannot work are refused, and nothing is saved.
    /// True: a typo shows its reason on the page.
    /// False: a broken address would be saved and fail on every event.
    /// </summary>
    [Theory]
    [InlineData("server")]
    [InlineData("contact")]
    [InlineData("from")]
    [InlineData("name")]
    [InlineData("url")]
    [InlineData("recipient")]
    public void Save_BadSettings_AreRefused(string what)
    {
        var settings = new NotificationSettings();
        switch (what)
        {
            case "server": settings.ServerUrl = "not a url"; break;
            case "contact": settings.PushContact = "someone"; break;
            case "from": settings.Smtp.FromAddress = "nobody"; break;
            case "name": settings.Channels.Add(new NotificationChannel { Kind = NotificationChannelKind.Discord }); break;
            case "url": settings.Channels.Add(new NotificationChannel { Name = "Hook", Kind = NotificationChannelKind.Webhook, Url = "ftp://example.com" }); break;
            default: settings.Channels.Add(new NotificationChannel { Name = "Mail", Kind = NotificationChannelKind.Email, Recipients = "not-an-address" }); break;
        }

        Assert.NotNull(Service().Save(settings, null));
        Assert.Equal(0, _config.Saves);
    }
}
