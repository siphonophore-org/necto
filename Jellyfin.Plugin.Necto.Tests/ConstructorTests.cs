using System.Linq;
using System.Reflection;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests;

/// <summary>
/// Jellyfin's service container refuses a class whose public constructors it can fill in more than one way, and it
/// ignores ActivatorUtilitiesConstructor. A second constructor for tests is internal.
/// </summary>
public class ConstructorTests
{
    /// <summary>
    /// Every service class has one public constructor.
    /// True: Jellyfin builds each service.
    /// False: a task or route fails with "The following constructors are ambiguous".
    /// </summary>
    [Fact]
    public void Services_HaveOnePublicConstructor()
    {
        var unmarked = typeof(Plugin).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Length > 1)
            .Where(t => t.Namespace?.StartsWith("Jellyfin.Plugin.Necto.Services", System.StringComparison.Ordinal) == true)
            .Select(t => t.FullName)
            .ToList();

        Assert.Empty(unmarked);
    }
}
