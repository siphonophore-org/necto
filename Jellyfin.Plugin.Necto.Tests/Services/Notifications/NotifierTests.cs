using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Configuration.Notifications;
using Jellyfin.Plugin.Necto.Services.Invites;
using Jellyfin.Plugin.Necto.Services.Notifications;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Siphonophore.Stolon;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Notifications;

/// <summary>
/// The notifier sends each event to every channel it goes to, writes each send to the log, and a channel that fails
/// never stops the rest or throws.
/// </summary>
public sealed class NotifierTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly FakeUsers _users = new();
    private readonly FakeConfigurationStore _config = new();
    private readonly FakeActivity _activity = new();
    private readonly FakeEmail _email = new();
    private readonly NotificationLog _log = new();
    private readonly SecretProtector _secrets = TestSecrets.Create();
    private JsonNotifyHeldStore? _held;
    private NotificationSecretStore? _kept;

    private NotificationSecretStore Kept => _kept ??= new NotificationSecretStore(_folder.File("secrets.json"), _secrets, null);

    public void Dispose() => _folder.Dispose();

    private Notifier Notifier()
    {
        var (sessions, stub) = InterfaceStub.Create<ISessionManager>();
        stub.Answer("get_Sessions", _ => new List<SessionInfo>());
        var (http, _) = InterfaceStub.Create<IHttpClientFactory>();
        _held = new JsonNotifyHeldStore(_folder.File("held.json"), time: TimeProvider.System);
        var push = NotificationServices.CreatePushStore(_config, _secrets, Kept, _folder.File("push.json"), NullLogger.Instance);
        var stolon = new StolonNotifier(http, sessions, NullLogger<StolonNotifier>.Instance, _secrets, _held, push);
        return new Notifier(stolon, _config, _users.Manager, _email, _activity.Logger, _secrets, Kept, _log, FakeServerConfig.Create(), NullLogger<Notifier>.Instance, push);
    }

    private void Channels(params NotificationChannel[] channels)
    {
        var settings = NotificationNormalizer.Normalize(new NotificationSettings());
        settings.Channels[0].AllEvents = false;
        settings.Channels.AddRange(channels);
        settings.Smtp = new SmtpSettings { Host = "smtp.example.com", FromAddress = "jellyfin@example.com", Password = _secrets.Protect("hunter2") };
        Kept.Keep(settings);
        _config.Configuration.Notifications = settings;
    }

    /// <summary>
    /// An event an email channel picks goes to its addresses, with the stored password.
    /// True: administrators get email for the events they chose.
    /// False: email would never be sent, or sent without signing in.
    /// </summary>
    [Fact]
    public async Task SendAsync_EmailChannel_SendsToItsAddresses()
    {
        Channels(new NotificationChannel { Id = "mail", Name = "Mail", Kind = NotificationChannelKind.Email, Recipients = "admin@example.com", EventKeys = { InvitesEvents.Used } });

        await Notifier().SendAsync(new NectoEvent(InvitesEvents.Used, "Invite used", "viewer joined"));

        var sent = Assert.Single(_email.Sent);
        Assert.Equal(new[] { "admin@example.com" }, sent.To);
        Assert.Equal("hunter2", sent.Password);
        Assert.Equal("Invite used", sent.Subject);
        Assert.True(Assert.Single(_log.List()).Ok);
    }

    /// <summary>
    /// A failed email is logged as not sent and the other channels still send.
    /// True: one broken channel never silences the rest.
    /// False: an SMTP failure would stop every other channel, or throw into the tool that sent the event.
    /// </summary>
    [Fact]
    public async Task SendAsync_FailingChannel_DoesNotStopTheRest()
    {
        Channels(
            new NotificationChannel { Id = "mail", Name = "Mail", Kind = NotificationChannelKind.Email, Recipients = "admin@example.com", AllEvents = true });
        _config.Configuration.Notifications.Channels[1].AllEvents = true;
        _email.Failure = new System.Net.Mail.SmtpException("refused");

        await Notifier().SendAsync(new NectoEvent(InvitesEvents.Used, "Invite used", "viewer joined"));

        Assert.Contains(_log.List(), e => !e.Ok && e.Channel == "Mail");
        Assert.Contains(_log.List(), e => e.Ok && e.EventKey == InvitesEvents.Used);
        Assert.Contains("Invite used", _activity.Names);
    }

    /// <summary>
    /// The activity log channel writes the event's title and body.
    /// True: an administrator can follow chosen events in Jellyfin's activity log.
    /// False: the activity log channel would write nothing.
    /// </summary>
    [Fact]
    public async Task SendAsync_ActivityLog_WritesTheEvent()
    {
        Channels();
        _config.Configuration.Notifications.Channels[1].EventKeys.Add(InvitesEvents.Locked);

        await Notifier().SendAsync(new NectoEvent(InvitesEvents.Locked, "Invite locked", "Too many PINs") { Level = NectoEventLevel.Warning });

        var entry = Assert.Single(_activity.Entries);
        Assert.Equal("Invite locked", entry.Name);
        Assert.Equal("Necto." + InvitesEvents.Locked, entry.Type);
    }

    /// <summary>
    /// A user's Jellyfin message is held while no app is online.
    /// True: a member hears about their invite the next time they open Jellyfin.
    /// False: a message to someone offline would be lost.
    /// </summary>
    [Fact]
    public async Task SendAsync_UserWithNoAppOnline_IsHeld()
    {
        Channels();
        var viewer = _users.Add("viewer");
        _config.Configuration.Groups.Add(TestGroups.Make("Family", p => p.Notifications.EventKeys.Add(InvitesEvents.Used), viewer.Id));
        var notifier = Notifier();

        await notifier.SendAsync(new NectoEvent(InvitesEvents.Used, "Invite used", "viewer joined") { UserId = viewer.Id });

        Assert.Equal(1, _held!.Counts()[viewer.Id]);
    }

    /// <summary>
    /// A test sends the test event to one channel, and an unknown channel is not found.
    /// True: each channel's test button reports what happened to that channel only.
    /// False: a test would go everywhere, or a bad id would throw.
    /// </summary>
    [Fact]
    public async Task TestAsync_SendsToOneChannel()
    {
        Channels(new NotificationChannel { Id = "mail", Name = "Mail", Kind = NotificationChannelKind.Email, Recipients = "admin@example.com" });
        var admin = _users.Add("admin", admin: true);
        var notifier = Notifier();

        var result = await notifier.TestAsync("mail", admin.Id, null, default);

        Assert.True(result!.Ok);
        Assert.Single(_email.Sent);
        Assert.True(Assert.Single(_log.List()).Test);
        Assert.Null(await notifier.TestAsync("nothing", admin.Id, null, default));
    }

    /// <summary>
    /// A test of browser notifications for someone with no browser says so.
    /// True: the page tells the administrator to turn notifications on first.
    /// False: the test would claim a notification that went nowhere.
    /// </summary>
    [Fact]
    public async Task TestAsync_BrowsersWithNoSubscription_SaysSo()
    {
        Channels();
        var admin = _users.Add("admin", admin: true);

        var result = await Notifier().TestAsync(NotificationRouter.BrowsersId, admin.Id, null, default);

        Assert.False(result!.Ok);
        Assert.Contains("browser", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// An email channel with no SMTP server set says so.
    /// True: the page names the fix.
    /// False: the test would fail with no reason.
    /// </summary>
    [Fact]
    public async Task TestAsync_EmailWithNoServer_SaysSo()
    {
        Channels(new NotificationChannel { Id = "mail", Name = "Mail", Kind = NotificationChannelKind.Email, Recipients = "admin@example.com" });
        _config.Configuration.Notifications.Smtp = new SmtpSettings();
        var admin = _users.Add("admin", admin: true);

        var result = await Notifier().TestAsync("mail", admin.Id, null, default);

        Assert.False(result!.Ok);
        Assert.Contains("SMTP", result.Message, StringComparison.Ordinal);
        Assert.Empty(_email.Sent);
    }
}
