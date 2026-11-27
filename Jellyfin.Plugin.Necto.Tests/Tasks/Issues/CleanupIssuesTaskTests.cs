using Jellyfin.Plugin.Necto.Tasks.Issues;
using Jellyfin.Plugin.Necto.Tasks.Requests;
using MediaBrowser.Model.Tasks;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Tasks.Issues;

/// <summary>
/// Issues clean up on a task of their own, apart from the requests clean up.
/// </summary>
public class CleanupIssuesTaskTests
{
    /// <summary>
    /// The issues clean up has the key Troubleshooting runs it by, its own name, and a daily trigger.
    /// True: Clean up issues in Troubleshooting runs only the issues clean up.
    /// False: the Issues row would run the requests clean up, or nothing.
    /// </summary>
    [Fact]
    public void Task_HasItsOwnKeyNameAndDailyTrigger()
    {
        var task = new CleanupIssuesTask(null!);

        Assert.Equal("IssuesCleanup", task.Key);
        Assert.Equal("Clean up issues", task.Name);
        Assert.Equal(TaskTriggerInfoType.DailyTrigger, Assert.Single(task.GetDefaultTriggers()).Type);
    }

    /// <summary>
    /// The requests clean up is named for requests alone.
    /// True: the scheduled tasks page and Troubleshooting name each clean up for what it cleans.
    /// False: two rows would read as the same task.
    /// </summary>
    [Fact]
    public void RequestsTask_IsNamedForRequests()
    {
        var task = new CleanupRequestsTask(null!, null!);

        Assert.Equal("RequestsCleanup", task.Key);
        Assert.Equal("Clean up requests", task.Name);
        Assert.NotEqual(new CleanupIssuesTask(null!).Name, task.Name);
    }
}
