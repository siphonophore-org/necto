using System.Linq;
using Jellyfin.Plugin.Necto.Services.Accounts;
using Jellyfin.Plugin.Necto.Tasks.Accounts;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Tasks.Accounts;

/// <summary>
/// The account tasks keep the keys Troubleshooting runs them by, the names the docs give, and their schedules.
/// </summary>
public class AccountTasksTests
{
    /// <summary>
    /// The expiry and inactivity task runs daily at midnight under its key and name.
    /// True: accounts expire once a day, and Troubleshooting finds the task.
    /// False: Run now would start nothing, or accounts would never expire.
    /// </summary>
    [Fact]
    public void AccountRulesTask_RunsDaily()
    {
        var users = new FakeUsers();
        var task = new AccountRulesTask(new AccountRuleService(new FakeConfigurationStore(), users.Manager, new FakeActivity().Logger, FakeServerConfig.Create(), NullLogger<AccountRuleService>.Instance), FakeServerConfig.Create());

        Assert.Equal("NectoAccountRules", task.Key);
        Assert.Equal("Process expired and inactive users", task.Name);
        Assert.Equal(TaskTriggerInfoType.DailyTrigger, Assert.Single(task.GetDefaultTriggers()).Type);
    }

    /// <summary>
    /// The session task runs daily under its key and name.
    /// True: stale devices are logged out once a day, and Troubleshooting finds the task.
    /// False: Run now would start nothing.
    /// </summary>
    [Fact]
    public void SessionCleanupTask_RunsDaily()
    {
        var users = new FakeUsers();
        var task = new SessionCleanupTask(new SessionCleanupService(new FakeConfigurationStore(), users.Manager, InterfaceStub.Create<MediaBrowser.Controller.Devices.IDeviceManager>().Proxy, InterfaceStub.Create<MediaBrowser.Controller.Session.ISessionManager>().Proxy, new FakeActivity().Logger, FakeServerConfig.Create(), NullLogger<SessionCleanupService>.Instance), FakeServerConfig.Create());

        Assert.Equal("NectoSessionCleanup", task.Key);
        Assert.Equal("Clean expired sessions", task.Name);
        Assert.Equal(TaskTriggerInfoType.DailyTrigger, task.GetDefaultTriggers().Single().Type);
    }
}
