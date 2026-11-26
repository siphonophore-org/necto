using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Siphonophore.Stolon;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests;

/// <summary>
/// The registrator registers Stolon's activity logger once.
/// </summary>
public class PluginServiceRegistratorTests
{
    /// <summary>
    /// The activity logger is registered as one singleton.
    /// True: every tool writes to the activity log through one logger.
    /// False: Jellyfin cannot build a service that writes to the activity log.
    /// </summary>
    [Fact]
    public void RegisterServices_ActivityLogger_IsOneSingleton()
    {
        var services = new ServiceCollection();

        new PluginServiceRegistrator().RegisterServices(services, null!);

        var logger = Assert.Single(services, d => d.ServiceType == typeof(ActivityLogger));
        Assert.Equal(ServiceLifetime.Singleton, logger.Lifetime);
    }
}
