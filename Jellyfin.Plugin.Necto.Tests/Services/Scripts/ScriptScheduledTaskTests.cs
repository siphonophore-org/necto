using System;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.Scripts;
using Jellyfin.Plugin.Necto.Services.Scripts;
using Jellyfin.Plugin.Necto.Tasks.Scripts;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Tasks;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Scripts;

public class ScriptScheduledTaskTests
{
    [Fact]
    public void AnIntervalScriptStartsWithAnIntervalTrigger()
    {
        var trigger = Assert.Single(ScriptScheduledTask.DefaultTriggers(new ScriptDefinition { ScheduleMode = ScheduleMode.Interval, ScheduleIntervalMinutes = 15 }));

        Assert.Equal(TaskTriggerInfoType.IntervalTrigger, trigger.Type);
        Assert.Equal(TimeSpan.FromMinutes(15).Ticks, trigger.IntervalTicks);
    }

    [Fact]
    public void ADailyScriptStartsDailyOrOnItsDays()
    {
        var script = new ScriptDefinition { ScheduleMode = ScheduleMode.Daily, ScheduleTime = "04:30" };
        var daily = Assert.Single(ScriptScheduledTask.DefaultTriggers(script));
        Assert.Equal(TaskTriggerInfoType.DailyTrigger, daily.Type);
        Assert.Equal(new TimeSpan(4, 30, 0).Ticks, daily.TimeOfDayTicks);

        script.ScheduleDays.AddRange(new[] { DayOfWeek.Monday, DayOfWeek.Friday });
        var weekly = ScriptScheduledTask.DefaultTriggers(script).ToList();
        Assert.Equal(new DayOfWeek?[] { DayOfWeek.Monday, DayOfWeek.Friday }, weekly.Select(t => t.DayOfWeek));
        Assert.All(weekly, t => Assert.Equal(TaskTriggerInfoType.WeeklyTrigger, t.Type));
    }

    [Fact]
    public void ANewScriptStartsWithNoTrigger()
    {
        var script = new ScriptDefinition { ScheduleTime = "04:30" };
        Assert.Equal(ScheduleMode.None, script.ScheduleMode);
        Assert.Empty(ScriptScheduledTask.DefaultTriggers(script));
    }

    [Fact]
    public void AMonthlyOrMissingScriptStartsWithNoTrigger()
    {
        Assert.Empty(ScriptScheduledTask.DefaultTriggers(new ScriptDefinition { ScheduleMode = ScheduleMode.Monthly }));
        Assert.Empty(ScriptScheduledTask.DefaultTriggers(null));
    }

    [Fact]
    public void EachScriptHasItsOwnStableTaskIdAndFiles()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var (paths, stub) = InterfaceStub.Create<IApplicationPaths>();
        stub.Answer("get_ConfigurationDirectoryPath", _ => Path.Combine("server", "config"));
        stub.Answer("get_DataPath", _ => Path.Combine("server", "data"));

        Assert.Equal(ScriptScheduledTasks.TaskIdFor(first), ScriptScheduledTasks.TaskIdFor(first));
        Assert.NotEqual(ScriptScheduledTasks.TaskIdFor(first), ScriptScheduledTasks.TaskIdFor(second));

        var files = ScriptScheduledTasks.TaskFiles(paths, first);
        var name = ScriptScheduledTasks.TaskIdFor(first) + ".js";
        Assert.Equal(
            new[] { Path.Combine("server", "config", "ScheduledTasks", name), Path.Combine("server", "data", "ScheduledTasks", name) },
            files);
    }
}
