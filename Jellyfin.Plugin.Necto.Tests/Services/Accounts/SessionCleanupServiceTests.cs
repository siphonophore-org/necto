using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities.Security;
using Jellyfin.Plugin.Necto.Configuration.Accounts;
using Jellyfin.Plugin.Necto.Services.Accounts;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Accounts;

/// <summary>
/// Session cleanup logs out a device only when every group of its user cleans its client and the device is past the
/// window, and never touches an administrator's devices.
/// </summary>
public class SessionCleanupServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeUsers _users = new();
    private readonly FakeConfigurationStore _config = new();
    private readonly FakeActivity _activity = new();
    private readonly List<Device> _devices = new();
    private readonly List<Device> _loggedOut = new();

    private Task<int> Run()
    {
        var (devices, deviceStub) = InterfaceStub.Create<IDeviceManager>();
        deviceStub.Answer("GetDevices", args =>
        {
            var userId = ((Jellyfin.Data.Queries.DeviceQuery)args[0]!).UserId;
            var items = _devices.Where(d => d.UserId == userId).ToList();
            return new QueryResult<Device>(items);
        });
        var (sessions, sessionStub) = InterfaceStub.Create<ISessionManager>();
        sessionStub.Answer("Logout", args =>
        {
            if (args[0] is Device device)
            {
                _loggedOut.Add(device);
            }

            return Task.CompletedTask;
        });
        return new SessionCleanupService(_config, _users.Manager, devices, sessions, _activity.Logger, FakeServerConfig.Create(), NullLogger<SessionCleanupService>.Instance, new FixedTime(Now))
            .CleanupAsync(null, CancellationToken.None);
    }

    private Device Device(Guid userId, string app, int daysAgo)
    {
        var device = new Device(userId, app, "1.0", "Phone", Guid.NewGuid().ToString("N")) { DateLastActivity = Now.UtcDateTime.AddDays(-daysAgo) };
        _devices.Add(device);
        return device;
    }

    private void Clean(int days, SessionClientMode mode = SessionClientMode.All, string[]? clients = null, params Guid[] members)
        => _config.Configuration.Groups.Add(TestGroups.Make("Clean", p =>
        {
            p.Accounts.Sessions.Enabled = true;
            p.Accounts.Sessions.Rules.Add(new SessionCleanupRule { Mode = mode, Days = days, Clients = (clients ?? Array.Empty<string>()).ToList() });
        }, members));

    /// <summary>
    /// A stale device on a covered client is logged out, a recent one stays, and the run writes one entry.
    /// True: only devices that stopped checking in are logged out.
    /// False: a device in use would be logged out, or a stale one kept.
    /// </summary>
    [Fact]
    public async Task Cleanup_StaleDevice_IsLoggedOut_RecentIsKept()
    {
        var viewer = _users.Add("viewer");
        Clean(30, members: viewer.Id);
        var stale = Device(viewer.Id, "Infuse", 40);
        Device(viewer.Id, "Swiftfin", 2);

        Assert.Equal(1, await Run());

        Assert.Equal(new[] { stale }, _loggedOut);
        Assert.Contains("Session logged out", _activity.Names);
    }

    /// <summary>
    /// A client no rule covers is kept.
    /// True: a rule for some clients leaves the others alone.
    /// False: every client would be logged out.
    /// </summary>
    [Fact]
    public async Task Cleanup_ClientNotCovered_IsKept()
    {
        var viewer = _users.Add("viewer");
        Clean(1, SessionClientMode.Only, new[] { "Jellyfin Web" }, viewer.Id);
        Device(viewer.Id, "Infuse", 40);

        Assert.Equal(0, await Run());
    }

    /// <summary>
    /// A user stored in a second group follows only the first, since a user is in one group.
    /// True: the first group's cleanup logs out the stale device.
    /// False: a stored second group would still change the rules a user follows.
    /// </summary>
    [Fact]
    public async Task Cleanup_UserStoredInASecondGroup_FollowsTheFirst()
    {
        var viewer = _users.Add("viewer");
        Clean(1, members: viewer.Id);
        _config.Configuration.Groups.Add(TestGroups.Make("Family", viewer.Id));
        Device(viewer.Id, "Infuse", 40);

        Assert.Equal(1, await Run());
    }

    /// <summary>
    /// An administrator's devices are never logged out.
    /// True: cleanup never signs out an administrator.
    /// False: an administrator in Everyone could be signed out of the dashboard.
    /// </summary>
    [Fact]
    public async Task Cleanup_Administrator_IsSkipped()
    {
        var admin = _users.Add("admin", admin: true);
        _config.Configuration.Everyone.Permissions.Accounts.Sessions.Enabled = true;
        _config.Configuration.Everyone.Permissions.Accounts.Sessions.Rules.Add(new SessionCleanupRule { Days = 1 });
        Device(admin.Id, "Jellyfin Web", 40);

        Assert.Equal(0, await Run());
    }

    /// <summary>
    /// The overview names each user and client once, with counts above one.
    /// True: one entry reads at a glance however many devices a run logs out.
    /// False: the entry would repeat users or show counts of 1.
    /// </summary>
    [Fact]
    public void CleanupOverview_NamesUsersAndClients()
    {
        var one = new Dictionary<string, IReadOnlyDictionary<string, int>> { ["viewer"] = new Dictionary<string, int> { ["Jellyfin Web"] = 1 } };
        var many = new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            ["viewer"] = new Dictionary<string, int> { ["Infuse"] = 2 },
            ["guest"] = new Dictionary<string, int> { ["Swiftfin"] = 1 }
        };

        Assert.Equal("Logged out viewer's stale session on Jellyfin Web", SessionCleanupService.CleanupOverview(one));
        Assert.Equal("Logged out 3 stale sessions for viewer on Infuse (2) and guest on Swiftfin", SessionCleanupService.CleanupOverview(many));
    }
}
