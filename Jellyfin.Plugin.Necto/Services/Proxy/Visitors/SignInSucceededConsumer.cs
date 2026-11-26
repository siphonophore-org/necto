using System;
using System.Threading.Tasks;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Events.Authentication;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Hosting;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Services.Proxy.Reachability;
using Jellyfin.Plugin.Necto.Api.Ddns;
using Jellyfin.Plugin.Necto.Api.Proxy;

namespace Jellyfin.Plugin.Necto.Services.Proxy.Visitors;

/// <summary>
/// Records successful sign-ins.
/// </summary>
public sealed class SignInSucceededConsumer : IEventConsumer<AuthenticationResultEventArgs>
{
    private readonly VisitorService _people;

    /// <summary>
    /// Initializes a new instance of the <see cref="SignInSucceededConsumer"/> class.
    /// </summary>
    /// <param name="people">The People service.</param>
    public SignInSucceededConsumer(VisitorService people)
    {
        _people = people;
    }

    /// <inheritdoc />
    public Task OnEvent(AuthenticationResultEventArgs eventArgs)
    {
        ArgumentNullException.ThrowIfNull(eventArgs);
        var user = eventArgs.User;
        var session = eventArgs.SessionInfo;
        _people.RecordSignIn(
            success: true,
            user?.Name ?? string.Empty,
            user is null ? string.Empty : user.Id.ToString("N"),
            session?.RemoteEndPoint,
            session?.DeviceId ?? string.Empty,
            session?.DeviceName ?? string.Empty,
            session?.Client ?? string.Empty);
        return Task.CompletedTask;
    }
}
