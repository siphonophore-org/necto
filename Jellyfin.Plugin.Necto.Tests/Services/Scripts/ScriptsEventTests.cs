using System;
using System.Linq;
using Jellyfin.Plugin.Necto.Services.Notifications;
using Jellyfin.Plugin.Necto.Services.Scripts;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Siphonophore.Stolon;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Scripts;

/// <summary>
/// A failed run sends scripts.failed to Notifications, at most once a minute for each script.
/// </summary>
public sealed class ScriptsEventTests
{
    /// <summary>
    /// The first failure of a script sends the event, a burst sends no more, and the next minute sends again.
    /// True: a library scan that fails a script many times sends one alert a minute.
    /// False: a burst floods every channel, or a failure never reaches Notifications.
    /// </summary>
    [Fact]
    public void RunFailedFor_SendsOneEventAMinuteForEachScript()
    {
        var notifier = new FakeNotifier();
        var now = new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc);
        using var activity = new ScriptsActivity(new ActivitySummary(new FakeActivity().Logger), notifier, null, () => now);

        activity.RunFailedFor("Notify", "Not an http or https URL");
        activity.RunFailedFor("Notify", "Not an http or https URL");
        activity.RunFailedFor("Backup", null);
        now = now.AddMinutes(1);
        activity.RunFailedFor("Notify", "again");

        Assert.Equal(3, notifier.Events.Count);
        Assert.All(notifier.Events, e => Assert.Equal(ScriptsEvents.Failed, e.Key));
        Assert.All(notifier.Events, e => Assert.Equal(NectoEventLevel.Error, e.Level));
        Assert.Equal(new[] { "Notify", "Backup", "Notify" }, notifier.Events.Select(e => e.Data["script"]));
        Assert.Contains("Notify", notifier.Events[0].Body, StringComparison.Ordinal);
    }
}
