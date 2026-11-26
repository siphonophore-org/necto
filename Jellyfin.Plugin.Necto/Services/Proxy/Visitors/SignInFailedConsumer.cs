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
/// Records failed sign-ins Jellyfin reports. Jellyfin only reports them for usernames that exist; the proxy
/// catches the rest.
/// </summary>
public sealed class SignInFailedConsumer : IEventConsumer<AuthenticationRequestEventArgs>
{
    private readonly VisitorService _people;

    /// <summary>
    /// Initializes a new instance of the <see cref="SignInFailedConsumer"/> class.
    /// </summary>
    /// <param name="people">The People service.</param>
    public SignInFailedConsumer(VisitorService people)
    {
        _people = people;
    }

    /// <inheritdoc />
    public Task OnEvent(AuthenticationRequestEventArgs eventArgs)
    {
        ArgumentNullException.ThrowIfNull(eventArgs);
        _people.RecordSignIn(
            success: false,
            eventArgs.Username ?? string.Empty,
            eventArgs.UserId is { } id && !id.Equals(Guid.Empty) ? id.ToString("N") : string.Empty,
            eventArgs.RemoteEndPoint,
            eventArgs.DeviceId ?? string.Empty,
            eventArgs.DeviceName ?? string.Empty,
            eventArgs.App ?? string.Empty);
        return Task.CompletedTask;
    }
}
