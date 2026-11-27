using System;
using System.IO;
using Jellyfin.Plugin.Necto.Configuration.Notifications;
using Jellyfin.Plugin.Necto.Services.Notifications;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Siphonophore.Stolon;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Notifications;

/// <summary>
/// The Notifications secrets file takes every secret out of the settings, gives each back to a copy, and is readable by
/// the server's account alone.
/// </summary>
public sealed class NotificationSecretStoreTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly SecretProtector _secrets = TestSecrets.Create();

    public void Dispose() => _folder.Dispose();

    private NotificationSecretStore Store() => new(_folder.File("secrets.json"), _secrets, null);

    private NotificationSettings Settings() => new()
    {
        Smtp = new SmtpSettings { Password = _secrets.Protect("hunter2") },
        Channels =
        {
            new NotificationChannel { Id = "hook", Kind = NotificationChannelKind.Webhook, Url = _secrets.Protect("https://hooks.example.com/x"), Headers = _secrets.Protect("Authorization: token") },
            new NotificationChannel { Id = "mail", Kind = NotificationChannelKind.Email, Recipients = "admin@example.com" }
        }
    };

    /// <summary>
    /// Keep clears every secret from the settings, and Fill gives each back to a copy.
    /// True: the configuration holds none, and a send still has every address and password.
    /// False: a secret would stay in the configuration, or a channel would send with no address.
    /// </summary>
    [Fact]
    public void Keep_ThenFill_RoundTripsEverySecret()
    {
        var settings = Settings();

        Store().Keep(settings);

        Assert.Empty(settings.Smtp.Password);
        Assert.All(settings.Channels, c => Assert.True(c.Url.Length == 0 && c.Headers.Length == 0 && c.Recipients.Length == 0));
        var filled = Store().Fill(settings);
        Assert.Equal("hunter2", _secrets.Unprotect(filled.Smtp.Password));
        Assert.Equal("https://hooks.example.com/x", _secrets.Unprotect(filled.Channels[0].Url));
        Assert.Equal("Authorization: token", _secrets.Unprotect(filled.Channels[0].Headers));
        Assert.Equal("admin@example.com", filled.Channels[1].Recipients);
    }

    /// <summary>
    /// The email addresses are encrypted in the file, and a saved settings change keeps the VAPID private key.
    /// True: the file holds no address in plain text, and saving settings never breaks browser notifications.
    /// False: the administrators' addresses would sit in plain text, or every save would drop the push key.
    /// </summary>
    [Fact]
    public void Keep_EncryptsAddresses_AndKeepsThePushKey()
    {
        Store().SetPushPrivateKey("enc:v1:push");

        Store().Keep(Settings());

        var text = File.ReadAllText(_folder.File("secrets.json"));
        Assert.DoesNotContain("admin@example.com", text, StringComparison.Ordinal);
        Assert.Equal("enc:v1:push", Store().Load().PushPrivateKey);
    }

    /// <summary>
    /// A secrets file written as plain JSON before the move to Stolon's protected store reads back whole, and the next
    /// save encrypts it.
    /// True: an upgrade keeps the SMTP password, the push key, and every channel's address.
    /// False: an upgrade would wipe every Notifications secret.
    /// </summary>
    [Fact]
    public void Load_ReadsAFileInTheOldFormat_AndTheNextSaveEncryptsIt()
    {
        var password = _secrets.Protect("hunter2");
        var url = _secrets.Protect("https://hooks.example.com/x");
        File.WriteAllText(_folder.File("secrets.json"), "{\"SmtpPassword\":\"" + password + "\",\"PushPrivateKey\":\"enc:v1:push\",\"Channels\":{\"hook\":{\"Url\":\"" + url + "\",\"Headers\":\"\",\"Recipients\":\"\"}}}");

        var read = Store().Load();
        Store().SetPushPrivateKey("enc:v1:next");

        Assert.Equal("hunter2", _secrets.Unprotect(read.SmtpPassword));
        Assert.Equal("enc:v1:push", read.PushPrivateKey);
        Assert.Equal("https://hooks.example.com/x", _secrets.Unprotect(read.Channels["hook"].Url));
        Assert.True(SecretProtector.IsProtected(File.ReadAllText(_folder.File("secrets.json"))));
        Assert.Equal("hunter2", _secrets.Unprotect(Store().Load().SmtpPassword));
    }

    /// <summary>
    /// A missing or unreadable file gives no secrets and does not throw.
    /// True: a fresh server, or a damaged file, still loads Notifications.
    /// False: Notifications would fail on every send until the file was fixed by hand.
    /// </summary>
    [Fact]
    public void Load_MissingOrBrokenFile_GivesNone()
    {
        Assert.Empty(Store().Load().Channels);

        File.WriteAllText(_folder.File("secrets.json"), "{ not json");

        Assert.Empty(Store().Load().SmtpPassword);
    }

    /// <summary>
    /// The file is readable by the server's account alone.
    /// True: another account on the machine cannot read the encrypted secrets.
    /// False: the secrets would be as open as any file in the folder.
    /// </summary>
    [Fact]
    public void Keep_WritesTheFileForItsOwnerOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Store().Keep(Settings());

        var mode = File.GetUnixFileMode(_folder.File("secrets.json"));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
    }
}
