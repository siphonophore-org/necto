using System;
using System.Linq;
using Jellyfin.Plugin.Necto.Services.Invites;
using Jellyfin.Plugin.Necto.Services.Notifications;
using Jellyfin.Plugin.Necto.Services.Policies;
using Jellyfin.Plugin.Necto.Tasks.Invites;
using Jellyfin.Plugin.Necto.Tasks.Policies;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Siphonophore.Stolon;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Tasks;

/// <summary>
/// The invite expiry and template tasks have the keys Troubleshooting runs them by, their names, and their triggers,
/// and every service Notifications and Invites need is registered once.
/// </summary>
public sealed class NectoTaskTests : IDisposable
{
    private readonly InviteHarness _h = new();

    public void Dispose() => _h.Dispose();

    /// <summary>
    /// The invite expiry task runs daily at midnight and turns off due invites.
    /// True: an invite past its day is turned off and logged once a day.
    /// False: expired invites would stay on in the Invites tool.
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task InviteExpiryTask_RunsDaily_AndExpiresInvites()
    {
        var task = new InviteExpiryTask(_h.Service, FakeServerConfig.Create());
        _h.Service.Create(new InviteRequest { ExpiresAt = DateTime.UtcNow.Date.AddDays(-2) }, Guid.Empty, null);

        await task.ExecuteAsync(new Progress<double>(), default);

        Assert.Equal("NectoInviteExpiry", task.Key);
        Assert.Equal("Disable expired invites", task.Name);
        Assert.Equal(TaskTriggerInfoType.DailyTrigger, Assert.Single(task.GetDefaultTriggers()).Type);
        Assert.False(_h.Config.Configuration.Invites.Single().Enabled);
    }

    /// <summary>
    /// The template task runs when the server starts.
    /// True: a change made in the dashboard is put back to the group's template on the next start.
    /// False: templates would drift from what groups set.
    /// </summary>
    [Fact]
    public void PolicyTemplatesTask_RunsAtStartupAndDaily()
    {
        var task = new PolicyTemplatesTask(_h.Templates, FakeServerConfig.Create());

        Assert.Equal("NectoPolicyTemplates", task.Key);
        Assert.Equal("Apply group Jellyfin settings", task.Name);
        Assert.Equal(new[] { TaskTriggerInfoType.StartupTrigger, TaskTriggerInfoType.DailyTrigger }, task.GetDefaultTriggers().Select(t => t.Type));
    }

    /// <summary>
    /// Notifications and Invites register each of their services once as a singleton.
    /// True: Jellyfin builds the notifier, the stores, and the invite service.
    /// False: a route or the notifier would fail to start.
    /// </summary>
    [Theory]
    [InlineData(typeof(INotifier), typeof(Notifier))]
    [InlineData(typeof(IEmailSender), typeof(SmtpEmailSender))]
    [InlineData(typeof(NotificationLog), typeof(NotificationLog))]
    [InlineData(typeof(NotificationSettingsService), typeof(NotificationSettingsService))]
    [InlineData(typeof(InviteService), typeof(InviteService))]
    [InlineData(typeof(InviteStatusStore), typeof(InviteStatusStore))]
    [InlineData(typeof(InviteAttemptLimiter), typeof(InviteAttemptLimiter))]
    [InlineData(typeof(PolicyTemplateService), typeof(PolicyTemplateService))]
    public void Services_AreRegisteredOnce(Type service, Type implementation)
    {
        var services = new ServiceCollection();
        new PluginServiceRegistrator().RegisterServices(services, null!);

        var descriptor = Assert.Single(services, d => d.ServiceType == service && d.ImplementationType == implementation);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.Contains(services, d => d.ServiceType == typeof(StolonNotifier));
        Assert.Contains(services, d => d.ServiceType == typeof(SecretProtector));
    }

    /// <summary>
    /// The web push and held message stores are Stolon's, each registered once as a singleton under its own type and
    /// under Stolon's interface.
    /// True: the notifier, the routes, and the user cleanup share one store of each.
    /// False: two stores could write the same file, or the notifier would fail to start.
    /// </summary>
    [Theory]
    [InlineData(typeof(IWebPushStore))]
    [InlineData(typeof(JsonWebPushStore))]
    [InlineData(typeof(INotifyHeldStore))]
    [InlineData(typeof(JsonNotifyHeldStore))]
    public void StolonStores_AreRegisteredOnce(Type service)
    {
        var services = new ServiceCollection();
        new PluginServiceRegistrator().RegisterServices(services, null!);

        var descriptor = Assert.Single(services, d => d.ServiceType == service);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.NotNull(descriptor.ImplementationFactory);
    }
}
