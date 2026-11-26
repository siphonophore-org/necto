using System.Linq;
using Jellyfin.Data.Events.Users;
using Jellyfin.Plugin.Necto.Services.Accounts;
using Jellyfin.Plugin.Necto.Services.Configuration;
using Jellyfin.Plugin.Necto.Services.Groups;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Events;
using Microsoft.Extensions.DependencyInjection;
using Siphonophore.Stolon;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests;

/// <summary>
/// The registrator registers Stolon's activity logger and every marked service, each once, under the type it asks for.
/// </summary>
public class PluginServiceRegistratorTests
{
    private static ServiceCollection Registered()
    {
        var services = new ServiceCollection();
        new PluginServiceRegistrator().RegisterServices(services, null!);
        return services;
    }

    /// <summary>
    /// The activity logger is registered as one singleton.
    /// True: every tool writes to the activity log through one logger.
    /// False: Jellyfin cannot build a service that writes to the activity log.
    /// </summary>
    [Fact]
    public void RegisterServices_ActivityLogger_IsOneSingleton()
    {
        var logger = Assert.Single(Registered(), d => d.ServiceType == typeof(ActivityLogger));
        Assert.Equal(ServiceLifetime.Singleton, logger.Lifetime);
    }

    /// <summary>
    /// The groups and account services are singletons under the types their callers ask for.
    /// True: Jellyfin builds the controller, the tasks, the provider, and the event consumers.
    /// False: a route or a task would fail to start for a missing service.
    /// </summary>
    [Theory]
    [InlineData(typeof(IConfigurationStore), typeof(ConfigurationStore))]
    [InlineData(typeof(GroupStore), typeof(GroupStore))]
    [InlineData(typeof(PermissionService), typeof(PermissionService))]
    [InlineData(typeof(PasswordEnrollment), typeof(PasswordEnrollment))]
    [InlineData(typeof(AccountRuleService), typeof(AccountRuleService))]
    [InlineData(typeof(SessionCleanupService), typeof(SessionCleanupService))]
    [InlineData(typeof(ResetCodeService), typeof(ResetCodeService))]
    [InlineData(typeof(AccountActivity), typeof(AccountActivity))]
    [InlineData(typeof(IAuthenticationProvider), typeof(PasswordRuleAuthenticationProvider))]
    [InlineData(typeof(IEventConsumer<UserCreatedEventArgs>), typeof(UserCreatedConsumer))]
    [InlineData(typeof(IEventConsumer<UserDeletedEventArgs>), typeof(UserDeletedConsumer))]
    public void RegisterServices_Service_IsOneSingleton(System.Type service, System.Type implementation)
    {
        var descriptor = Assert.Single(Registered(), d => d.ServiceType == service);

        Assert.Equal(implementation, descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }
}
