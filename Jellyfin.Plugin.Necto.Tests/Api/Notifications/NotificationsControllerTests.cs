using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Api;
using Jellyfin.Plugin.Necto.Api.Notifications;
using Jellyfin.Plugin.Necto.Services.Notifications;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using MediaBrowser.Controller.Session;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Siphonophore.Stolon;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Api.Notifications;

/// <summary>
/// A signed in user turns browser notifications on and off for themselves only, the page gets the key while browser
/// notifications are on, and the worker may control the server's root.
/// </summary>
public sealed class NotificationsControllerTests : IDisposable
{
    private const string P256dh = "BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4";
    private const string Auth = "BTBZMqHH6r4Tts7J_aSIgg";

    private readonly TempFolder _folder = new();
    private readonly FakeUsers _users = new();
    private readonly FakeConfigurationStore _config = new();
    private readonly SecretProtector _secrets = TestSecrets.Create();
    private JsonWebPushStore? _push;
    private NotificationSecretStore? _kept;

    public void Dispose() => _folder.Dispose();

    private NotificationsController Controller(Guid user)
    {
        var (sessions, stub) = InterfaceStub.Create<ISessionManager>();
        stub.Answer("get_Sessions", _ => new List<SessionInfo>());
        var (http, _) = InterfaceStub.Create<IHttpClientFactory>();
        var held = new JsonNotifyHeldStore(_folder.File("held.json"), time: TimeProvider.System);
        _kept ??= new NotificationSecretStore(_folder.File("secrets.json"), _secrets, null);
        _push ??= NotificationServices.CreatePushStore(_config, _secrets, _kept, _folder.File("push.json"), NullLogger.Instance);
        var stolon = new StolonNotifier(http, sessions, NullLogger<StolonNotifier>.Instance, _secrets, held, _push);
        var log = new NotificationLog();
        var notifier = new Notifier(stolon, _config, _users.Manager, new FakeEmail(), new FakeActivity().Logger, _secrets, _kept, log, FakeServerConfig.Create(), NullLogger<Notifier>.Instance, _push);
        var controller = new NotificationsController(notifier, stolon, new NotificationSettingsService(_config, _secrets, _kept), _kept, log, _config, _users.Manager, _push, held);
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(Caller.UserIdClaim, user.ToString()) }, "test"))
        };
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        return controller;
    }

    private PushController Push(Guid user)
    {
        Controller(user);
        var (sessions, stub) = InterfaceStub.Create<ISessionManager>();
        stub.Answer("get_Sessions", _ => new List<SessionInfo>());
        var (http, _) = InterfaceStub.Create<IHttpClientFactory>();
        var held = new JsonNotifyHeldStore(_folder.File("held.json"), time: TimeProvider.System);
        var stolon = new StolonNotifier(http, sessions, NullLogger<StolonNotifier>.Instance, _secrets, held, _push);
        var controller = new PushController(stolon, _config);
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(Caller.UserIdClaim, user.ToString()) }, "test"))
        };
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        return controller;
    }

    private static PushSubscriptionBody Browser(string endpoint) => new() { Endpoint = endpoint, Keys = new PushSubscriptionKeys { P256dh = P256dh, Auth = Auth }, Label = "Safari" };

    /// <summary>
    /// A user's subscription is kept for them, and only they can remove it.
    /// True: each user turns notifications on and off for themselves only.
    /// False: one user could turn off another's browser, or subscribe a browser as someone else.
    /// </summary>
    [Fact]
    public void Subscribe_IsForTheCallerOnly()
    {
        var viewer = Guid.NewGuid();
        var other = Guid.NewGuid();

        Assert.IsType<NoContentResult>(Push(viewer).Subscribe(Browser("https://push.example.com/1")));

        Assert.Single(_push!.GetSubscriptions(viewer));
        Assert.Empty(_push.GetSubscriptions(other));
        Assert.IsType<NotFoundResult>(Push(other).Unsubscribe(Browser("https://push.example.com/1")));
        Assert.Single(_push.GetSubscriptions(viewer));
        Assert.IsType<NoContentResult>(Push(viewer).Unsubscribe(Browser("https://push.example.com/1")));
        Assert.Empty(_push.GetSubscriptions(viewer));
    }

    /// <summary>
    /// A subscription that is not https or has bad keys is refused.
    /// True: only a real push service address is kept.
    /// False: a page could make the server post to any address.
    /// </summary>
    [Fact]
    public void Subscribe_BadSubscription_IsRefused()
    {
        Assert.IsType<BadRequestResult>(Push(Guid.NewGuid()).Subscribe(Browser("http://push.example.com/1")));
        Assert.IsType<BadRequestResult>(Push(Guid.NewGuid()).Subscribe(new PushSubscriptionBody { Endpoint = "https://push.example.com/1" }));
    }

    /// <summary>
    /// The key is given while browser notifications are on, and not while they are off.
    /// True: user pages hide the switch while Settings turns browser notifications off.
    /// False: users could subscribe to notifications that never come.
    /// </summary>
    [Fact]
    public void PushKey_FollowsTheSetting()
    {
        var key = Assert.IsType<PushKeyView>(Assert.IsType<OkObjectResult>(Push(Guid.NewGuid()).PushKey().Result).Value);
        Assert.False(string.IsNullOrEmpty(key.PublicKey));

        _config.Configuration.Notifications.BrowserNotifications = false;
        Assert.IsType<NotFoundResult>(Push(Guid.NewGuid()).PushKey().Result);
    }

    /// <summary>
    /// The worker is served as JavaScript that may control the server's root.
    /// True: one registration and one subscription serve every user page.
    /// False: each user page would need its own worker and subscription.
    /// </summary>
    [Fact]
    public void Worker_MayControlTheRoot()
    {
        var controller = Controller(Guid.NewGuid());

        var worker = controller.Worker();

        Assert.Equal("/", controller.Response.Headers["Service-Worker-Allowed"].ToString());
        Assert.Contains("showNotification", worker.Content, StringComparison.Ordinal);
    }

    /// <summary>
    /// The settings route hides secrets and a refused save answers 400 with the reason.
    /// True: the page shows why a save was refused and never sees a stored secret.
    /// False: a bad address would be saved, or a secret would reach the page.
    /// </summary>
    [Fact]
    public void Settings_HideSecretsAndRefuseBadSaves()
    {
        var refused = Controller(Guid.NewGuid()).SaveSettings(new Jellyfin.Plugin.Necto.Configuration.Notifications.NotificationSettings { ServerUrl = "nope" });

        Assert.IsType<ApiError>(Assert.IsType<BadRequestObjectResult>(refused.Result).Value);
        var read = Assert.IsType<Jellyfin.Plugin.Necto.Configuration.Notifications.NotificationSettings>(Assert.IsType<OkObjectResult>(Controller(Guid.NewGuid()).GetSettings().Result).Value);
        Assert.Empty(read.PushPrivateKey);
    }
}
