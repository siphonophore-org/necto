using System;
using System.Threading.Tasks;
using Jellyfin.Data.Events.Users;
using Jellyfin.Plugin.Necto.Services.Accounts;
using Jellyfin.Plugin.Necto.Services.Groups;
using Jellyfin.Plugin.Necto.Services.Invites;
using Jellyfin.Plugin.Necto.Services.Policies;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.Necto.Tests.Fakes;

/// <summary>
/// Builds the invite service over in memory users, groups, counters, and events. A user the service makes fires the
/// new user event, as Jellyfin does, so the hold on an invited account runs as it does on a server.
/// </summary>
public sealed class InviteHarness : IDisposable
{
    private readonly TempFolder _folder = new();

    public InviteHarness(DateTimeOffset? now = null)
    {
        Time = new FixedTime(now ?? new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero));
        var server = FakeServerConfig.Create();
        var enrollment = new PasswordEnrollment(Config, Users.Manager, new AccountActivity(Activity.Logger, server), NullLogger<PasswordEnrollment>.Instance);
        Templates = new PolicyTemplateService(Config, Users.Manager, NullLogger<PolicyTemplateService>.Instance);
        Status = new InviteStatusStore(_folder.File("status.json"), null);
        Service = new InviteService(Config, Status, Users.Manager, new FakeCrypto(), enrollment, Templates, Activity.Logger, new InviteActivity(Activity.Logger, server), Notifier, server, NullLogger<InviteService>.Instance, Time);
        var created = new UserCreatedConsumer(Config, enrollment, Templates, Users.Manager, NullLogger<UserCreatedConsumer>.Instance);
        Users.OnCreated = user =>
        {
            DisabledWhenCreated = null;
            created.OnEvent(new UserCreatedEventArgs(user)).GetAwaiter().GetResult();
            DisabledWhenCreated = Users.IsDisabled(user.Id);
        };
    }

    public FakeUsers Users { get; } = new();

    public FakeConfigurationStore Config { get; } = new();

    public FakeActivity Activity { get; } = new();

    public FakeNotifier Notifier { get; } = new();

    public FixedTime Time { get; }

    public PolicyTemplateService Templates { get; }

    public InviteStatusStore Status { get; }

    public InviteService Service { get; }

    public bool? DisabledWhenCreated { get; private set; }

    public Task<InviteRedeemResult> RedeemAsync(string token, string username = "viewer", string password = "Secret1!", string pin = "")
        => Service.RedeemAsync(token, pin, username, password, null, default);

    public void Dispose()
    {
        Service.Dispose();
        _folder.Dispose();
    }
}
