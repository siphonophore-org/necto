using System;
using System.Linq;
using Jellyfin.Plugin.Necto.Services.Notifications;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Siphonophore.Stolon;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Notifications;

/// <summary>
/// Necto makes its VAPID keys once and keeps the private key encrypted, keeps each browser's subscription per user,
/// keeps held Jellyfin messages until an app comes online, and keeps the latest sends for Troubleshooting.
/// </summary>
public sealed class NotificationStoresTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly FakeConfigurationStore _config = new();
    private readonly SecretProtector _secrets = TestSecrets.Create();

    public void Dispose() => _folder.Dispose();

    private NotificationPushStore Push() => new(_config, _secrets, _folder.File("push.json"), NullLogger.Instance);

    private static WebPushSubscription Browser(Guid user, string endpoint) => new(user, endpoint, "p", "a", "Safari", DateTime.UtcNow);

    /// <summary>
    /// The key pair is made the first time it is needed, kept with the private key encrypted, and read back the same.
    /// True: browsers stay subscribed across restarts.
    /// False: a new key every start would break every subscription, or the private key would sit in plain text.
    /// </summary>
    [Fact]
    public void GetKeys_MakesThePairOnce_AndKeepsItEncrypted()
    {
        var first = Push().GetKeys();
        var second = Push().GetKeys();

        Assert.NotNull(first);
        Assert.Equal(first!.PublicKey, second!.PublicKey);
        Assert.Equal(first.PublicKey, _config.Configuration.Notifications.PushPublicKey);
        Assert.True(SecretProtector.IsProtected(_config.Configuration.Notifications.PushPrivateKey));
    }

    /// <summary>
    /// Resetting the keys makes a new pair and forgets every browser.
    /// True: Troubleshooting can recover from a broken key pair.
    /// False: browsers would keep subscriptions no push can reach.
    /// </summary>
    [Fact]
    public void ResetKeys_MakesANewPair_AndForgetsEveryBrowser()
    {
        var push = Push();
        var before = push.GetKeys()!.PublicKey;
        push.Save(Browser(Guid.NewGuid(), "https://push.example.com/1"));

        Assert.Equal(1, push.ResetKeys());

        Assert.NotEqual(before, push.GetKeys()!.PublicKey);
        Assert.Empty(push.All());
    }

    /// <summary>
    /// A subscription is kept per user, once per endpoint, and a user's removal forgets only theirs.
    /// True: each user's browsers get only their own notifications.
    /// False: one user's browser could get another's, or a deleted user's browsers would stay.
    /// </summary>
    [Fact]
    public void Subscriptions_ArePerUserAndPerEndpoint()
    {
        var push = Push();
        var viewer = Guid.NewGuid();
        var other = Guid.NewGuid();
        push.Save(Browser(viewer, "https://push.example.com/1"));
        push.Save(Browser(viewer, "https://push.example.com/1"));
        push.Save(Browser(other, "https://push.example.com/2"));

        Assert.Single(push.GetSubscriptions(viewer));
        push.RemoveUser(viewer);
        Assert.Empty(push.GetSubscriptions(viewer));
        Assert.Single(push.GetSubscriptions(other));
    }

    /// <summary>
    /// Held messages come back once, oldest first, and an old one is dropped.
    /// True: a user who comes online sees each waiting message once.
    /// False: a message would show twice, or the file would grow without end.
    /// </summary>
    [Fact]
    public void Held_IsTakenOnce_AndDropsOldMessages()
    {
        var now = new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
        var held = new NotificationHeldStore(_folder.File("held.json"), null, new FixedTime(now));
        var user = Guid.NewGuid();
        held.Hold(new NotifyHeldMessage(Guid.NewGuid(), user, "Second", "b", now.UtcDateTime.AddMinutes(-1)));
        held.Hold(new NotifyHeldMessage(Guid.NewGuid(), user, "First", "a", now.UtcDateTime.AddMinutes(-2)));
        held.Hold(new NotifyHeldMessage(Guid.NewGuid(), user, "Old", "c", now.UtcDateTime.AddDays(-31)));

        Assert.Equal(new[] { "First", "Second" }, held.Take(user).Select(m => m.Header));
        Assert.Empty(held.Take(user));
    }

    /// <summary>
    /// A user keeps at most the newest messages past the cap.
    /// True: someone who never signs in cannot fill the disk.
    /// False: held messages would pile up without end.
    /// </summary>
    [Fact]
    public void Held_KeepsTheNewestPastTheCap()
    {
        var now = new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
        var held = new NotificationHeldStore(_folder.File("held.json"), null, new FixedTime(now));
        var user = Guid.NewGuid();
        for (var i = 0; i < NotificationHeldStore.MaxPerUser + 5; i++)
        {
            held.Hold(new NotifyHeldMessage(Guid.NewGuid(), user, i.ToString(System.Globalization.CultureInfo.InvariantCulture), string.Empty, now.UtcDateTime.AddSeconds(i - 100)));
        }

        Assert.Equal(NotificationHeldStore.MaxPerUser, held.Counts()[user]);
        Assert.Equal(NotificationHeldStore.MaxPerUser, held.Clear());
    }

    /// <summary>
    /// The send log keeps the newest entries first, up to its capacity, and clears.
    /// True: Troubleshooting shows the latest sends.
    /// False: the log would grow without end or show the oldest first.
    /// </summary>
    [Fact]
    public void Log_KeepsTheNewestFirst_UpToItsCapacity()
    {
        var log = new NotificationLog();
        for (var i = 0; i < NotificationLog.Capacity + 3; i++)
        {
            log.Add(new NotificationLogEntry(DateTime.UtcNow, "e" + i, "t", "c", true, "Sent."));
        }

        Assert.Equal(NotificationLog.Capacity, log.List().Count);
        Assert.Equal("e" + (NotificationLog.Capacity + 2), log.List()[0].EventKey);
        Assert.Equal(NotificationLog.Capacity, log.Clear());
        Assert.Empty(log.List());
    }
}
