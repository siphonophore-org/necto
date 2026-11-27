using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Fakes;

/// <summary>
/// The tests that load the plugin, whose settings every Requests and Issues service reads through one static
/// instance. They run one after another, after every other test, so no test swaps the settings under another.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ServicesCollection
{
    public const string Name = "Services";
}
