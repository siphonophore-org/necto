using Jellyfin.Plugin.Necto.Services.Dashboard;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Dashboard;

/// <summary>
/// Necto's own activity log entries are told from the rest by the types its tools write.
/// </summary>
public class NectoActivityTests
{
    /// <summary>
    /// Every type a Necto tool writes is Necto's, and a type that only starts with the same letters is not.
    /// True: Recent activity lists every tool's entries and no other plugin's.
    /// False: Jellyfin's own entries, or another plugin's, would show on the Dashboard.
    /// </summary>
    [Theory]
    [InlineData("Necto.Proxy.AddressBlocked", true)]
    [InlineData("Necto.proxy.banned", true)]
    [InlineData("Requests", true)]
    [InlineData("Requests.Approved", true)]
    [InlineData("WireGuard", true)]
    [InlineData("Storage.DiskMissing", true)]
    [InlineData("Scripts.Script", true)]
    [InlineData("NectoOther", false)]
    [InlineData("RequestsPlus.Thing", false)]
    [InlineData("SessionStarted", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Owns_TellsNectoTypes(string? type, bool expected)
        => Assert.Equal(expected, NectoActivity.Owns(type));
}
