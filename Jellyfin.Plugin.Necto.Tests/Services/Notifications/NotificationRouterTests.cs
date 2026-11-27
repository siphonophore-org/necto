using System;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Notifications;
using Jellyfin.Plugin.Necto.Services.Invites;
using Jellyfin.Plugin.Necto.Services.Notifications;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Notifications;

/// <summary>
/// An event goes to the user it is about when their groups give it to them, and to each administrators' channel that
/// picks it, and to nobody else.
/// </summary>
public class NotificationRouterTests
{
    private static readonly Guid Viewer = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();

    private static NotificationUser[] Users(bool viewerDisabled = false) => new[]
    {
        new NotificationUser(Viewer, false, viewerDisabled),
        new NotificationUser(Admin, true, false)
    };

    private static PluginConfiguration Config(Action<NotificationSettings>? settings = null, params string[] viewerEvents)
    {
        var group = TestGroups.Make("Family", p => p.Notifications.EventKeys.AddRange(viewerEvents), Viewer);
        var config = TestGroups.Config(group);
        config.Notifications = NotificationNormalizer.Normalize(new NotificationSettings());
        config.Notifications.Channels[0].AllEvents = false;
        settings?.Invoke(config.Notifications);
        return config;
    }

    private static NectoEvent Used(Guid? user) => new(InvitesEvents.Used, "Invite used", "viewer joined") { UserId = user };

    /// <summary>
    /// A user facing event goes to its user's browsers and apps when their groups give it.
    /// True: a member hears about their own invite being used.
    /// False: a member who asked for the event would never get it.
    /// </summary>
    [Fact]
    public void Plan_UserEventTheGroupsGive_GoesToTheUsersBrowsersAndApps()
    {
        var sends = NotificationRouter.Plan(Config(null, InvitesEvents.Used), Used(Viewer), Users());

        Assert.Equal(new[] { NotificationTarget.UserBrowsers, NotificationTarget.UserApps }, sends.Select(s => s.Target));
        Assert.All(sends, s => Assert.Equal(new[] { Viewer }, s.Recipients));
    }

    /// <summary>
    /// A user whose groups do not give the event gets nothing.
    /// True: groups decide who hears an event.
    /// False: every user would be sent every event about them.
    /// </summary>
    [Fact]
    public void Plan_EventTheGroupsDoNotGive_ReachesNoUser()
    {
        Assert.Empty(NotificationRouter.Plan(Config(), Used(Viewer), Users()));
    }

    /// <summary>
    /// A disabled account gets nothing, even when its groups give the event.
    /// True: a disabled user is never notified.
    /// False: a disabled account would keep getting notifications.
    /// </summary>
    [Fact]
    public void Plan_DisabledUser_GetsNothing()
    {
        Assert.Empty(NotificationRouter.Plan(Config(null, InvitesEvents.Used), Used(Viewer), Users(viewerDisabled: true)));
    }

    /// <summary>
    /// An event for administrators only never goes to the user it names.
    /// True: an administrators' event stays with the administrators' channels.
    /// False: a user would see events meant for administrators.
    /// </summary>
    [Fact]
    public void Plan_AdministratorsEvent_NeverGoesToTheUser()
    {
        var expired = new NectoEvent(InvitesEvents.Expired, "Invites expired", string.Empty) { UserId = Viewer };

        Assert.Empty(NotificationRouter.Plan(Config(null, InvitesEvents.Expired), expired, Users()));
    }

    /// <summary>
    /// Turning off browser notifications or Jellyfin messages drops that channel only.
    /// True: each user channel is switched on its own.
    /// False: a channel turned off in Settings would still send.
    /// </summary>
    [Fact]
    public void Plan_UserChannelTurnedOff_IsLeftOut()
    {
        var sends = NotificationRouter.Plan(Config(s => s.BrowserNotifications = false, InvitesEvents.Used), Used(Viewer), Users());

        Assert.Equal(NotificationTarget.UserApps, Assert.Single(sends).Target);
    }

    /// <summary>
    /// Each administrators' channel that picks the event gets it, and one that does not, or is off, gets nothing.
    /// True: each channel picks its own events.
    /// False: a channel would get events it did not pick.
    /// </summary>
    [Fact]
    public void Plan_AdministratorsChannels_GetOnlyTheEventsTheyPick()
    {
        var config = Config(s =>
        {
            s.Channels.Add(new NotificationChannel { Id = "d", Name = "Discord", Kind = NotificationChannelKind.Discord, EventKeys = { InvitesEvents.Used } });
            s.Channels.Add(new NotificationChannel { Id = "w", Name = "Hook", Kind = NotificationChannelKind.Webhook, EventKeys = { InvitesEvents.Locked } });
            s.Channels.Add(new NotificationChannel { Id = "e", Name = "Mail", Kind = NotificationChannelKind.Email, AllEvents = true, Enabled = false });
        });

        var sends = NotificationRouter.Plan(config, Used(null), Users());

        Assert.Equal("d", Assert.Single(sends).ChannelId);
    }

    /// <summary>
    /// The Administrators channel goes to every administrator, and once only to an administrator who gets it as a user.
    /// True: an administrator never gets the same event twice.
    /// False: an administrator would see a doubled notification.
    /// </summary>
    [Fact]
    public void Plan_AdministratorsChannel_SkipsAnAdministratorWhoGetsItAsAUser()
    {
        var config = Config(s => s.Channels[0].AllEvents = true);

        var forViewer = NotificationRouter.Plan(config, Used(Viewer), Users());
        var forAdmin = NotificationRouter.Plan(config, Used(Admin), Users());

        Assert.Equal(new[] { Admin }, forViewer.Single(s => s.Target == NotificationTarget.Administrators).Recipients);
        Assert.DoesNotContain(forAdmin, s => s.Target == NotificationTarget.Administrators);
        Assert.Contains(forAdmin, s => s.Target == NotificationTarget.UserApps && s.Recipients.Contains(Admin));
    }

    /// <summary>
    /// The activity log channel gets the events it picks.
    /// True: an event can be written to Jellyfin's activity log.
    /// False: the activity log channel would never be used.
    /// </summary>
    [Fact]
    public void Plan_ActivityLogChannel_GetsItsEvents()
    {
        var config = Config(s => s.Channels[1].EventKeys.Add(InvitesEvents.Used));

        Assert.Equal(NotificationTarget.ActivityLog, Assert.Single(NotificationRouter.Plan(config, Used(null), Users())).Target);
    }

    /// <summary>
    /// Each other user an event names gets it too, when their groups give it, once each.
    /// True: everyone who joined a request hears about it.
    /// False: only the first user an event names would hear about it.
    /// </summary>
    [Fact]
    public void Plan_OtherUsers_GetItWhenTheirGroupsGiveIt()
    {
        var stranger = Guid.NewGuid();
        var users = Users().Append(new NotificationUser(stranger, false, false)).ToArray();
        var e = Used(Viewer) with { OtherUserIds = new[] { Viewer, Admin, stranger } };

        var apps = NotificationRouter.Plan(Config(null, InvitesEvents.Used), e, users).Single(s => s.Target == NotificationTarget.UserApps);

        Assert.Equal(new[] { Viewer, Admin }, apps.Recipients);
    }
}
