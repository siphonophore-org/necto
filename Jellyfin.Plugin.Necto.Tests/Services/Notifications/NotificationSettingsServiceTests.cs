using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.Notifications;
using Jellyfin.Plugin.Necto.Services.Notifications;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Siphonophore.Stolon;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Notifications;

/// <summary>
/// The settings reach a page with every secret hidden, a save keeps each secret sent back as kept, and settings that
/// would not work are refused with a reason.
/// </summary>
public class NotificationSettingsServiceTests
{
    private readonly FakeConfigurationStore _config = new();
    private readonly SecretProtector _secrets = TestSecrets.Create();

    private NotificationSettingsService Service() => new(_config, _secrets);

    private static NotificationSettings WithHook(string url, string headers = "") => new()
    {
        Channels = { new NotificationChannel { Id = "hook", Name = "Hook", Kind = NotificationChannelKind.Webhook, Url = url, Headers = headers } },
        Smtp = new SmtpSettings { Host = "smtp.example.com", FromAddress = "jellyfin@example.com", Password = "hunter2" }
    };

    /// <summary>
    /// A saved address, header, and password are stored encrypted and read back hidden.
    /// True: no secret ever reaches the browser or sits in the configuration in plain text.
    /// False: a webhook token or SMTP password would leak.
    /// </summary>
    [Fact]
    public void Save_EncryptsSecrets_AndReadHidesThem()
    {
        Assert.Null(Service().Save(WithHook("https://hooks.example.com/x", "Authorization: token"), null));

        var stored = _config.Configuration.Notifications;
        var hook = stored.Channels.Single(c => c.Id == "hook");
        Assert.True(SecretProtector.IsProtected(hook.Url));
        Assert.Equal("https://hooks.example.com/x", _secrets.Unprotect(hook.Url));
        Assert.Equal("hunter2", _secrets.Unprotect(stored.Smtp.Password));

        var read = Service().Read();
        Assert.Equal(SecretProtector.KeptSentinel, read.Channels.Single(c => c.Id == "hook").Url);
        Assert.Equal(SecretProtector.KeptSentinel, read.Smtp.Password);
        Assert.Empty(read.PushPrivateKey);
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

        var stored = _config.Configuration.Notifications;
        Assert.Equal("https://hooks.example.com/x", _secrets.Unprotect(stored.Channels.Single(c => c.Id == "hook").Url));
        Assert.Equal("Authorization: token", _secrets.Unprotect(stored.Channels.Single(c => c.Id == "hook").Headers));
        Assert.Equal("hunter2", _secrets.Unprotect(stored.Smtp.Password));
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
        _config.Configuration.Notifications.PushPrivateKey = "private";

        Service().Save(new NotificationSettings { PushPublicKey = "other", PushPrivateKey = "other" }, null);

        Assert.Equal("public", _config.Configuration.Notifications.PushPublicKey);
        Assert.Equal("private", _config.Configuration.Notifications.PushPrivateKey);
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
