using System;
using System.Linq;
using System.Net.Http;
using Jellyfin.Plugin.Necto.Services;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Activity;
using Microsoft.Extensions.Hosting;
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
[Collection(ServicesCollection.Name)]
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

    /// <summary>
    /// DI builds every Requests and Issues service from the registrator, over stand-ins for what Jellyfin supplies.
    /// True: Jellyfin starts every Requests and Issues service, the controller's and the hosted ones alike.
    /// False: a service would ask for something nothing registers, and the plugin would fail to start.
    /// </summary>
    [Fact]
    public void RegisterServices_RequestsAndIssues_AllBuild()
    {
        var services = Registered();
        Microsoft.Extensions.DependencyInjection.LoggingServiceCollectionExtensions.AddLogging(services);
        services.AddSingleton(InterfaceStub.Of<IUserManager>());
        services.AddSingleton(InterfaceStub.Of<ILibraryManager>());
        services.AddSingleton(InterfaceStub.Of<IUserDataManager>());
        services.AddSingleton(InterfaceStub.Of<ILibraryMonitor>());
        services.AddSingleton(InterfaceStub.Of<IDtoService>());
        services.AddSingleton(InterfaceStub.Of<IHttpClientFactory>());
        services.AddSingleton(InterfaceStub.Of<IPluginManager>());
        services.AddSingleton(InterfaceStub.Of<IServerConfigurationManager>());
        services.AddSingleton(InterfaceStub.Of<IServerApplicationHost>());
        services.AddSingleton(InterfaceStub.Of<IActivityManager>());
        var paths = InterfaceStub.Of<IServerApplicationPaths>();
        services.AddSingleton(paths);
        services.AddSingleton<MediaBrowser.Common.Configuration.IApplicationPaths>(paths);
        var tools = PluginServiceRegistrator.DiscoverAnnotatedServices(typeof(Plugin).Assembly)
            .Where(s => s.Impl.Namespace is "Jellyfin.Plugin.Necto.Services.Requests" or "Jellyfin.Plugin.Necto.Services.Issues" or "Jellyfin.Plugin.Necto.Services.Notifications")
            .ToList();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = false, ValidateScopes = true });

        var hosted = provider.GetServices<IHostedService>().Select(h => h.GetType()).ToList();

        Assert.True(tools.Count >= 20, "Only " + tools.Count + " Requests and Issues services are marked.");
        Assert.All(tools, s =>
        {
            if (s.Attr.ServiceType == typeof(IHostedService))
            {
                Assert.Contains(s.Impl, hosted);
            }
            else
            {
                Assert.IsType(s.Impl, provider.GetRequiredService(s.Attr.ServiceType ?? s.Impl));
            }
        });
    }
}
