using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Necto.Services.Requests;

/// <summary>
/// Checks the request page against the installed web client once at startup, through Stolon's check, and warns about
/// classes it no longer styles, a changed web client, and a details page template that lacks a part.
/// </summary>
[PluginService(ServiceLifetime.Singleton, ServiceType = typeof(IHostedService))]
public sealed class WebClientCheck : IHostedService
{
    /// <summary>
    /// The web client's card shapes the page passes to Stolon's builders, such as <c>'square'</c>, whose classes Stolon writes
    /// from parts, so neither reading the markup nor Stolon's own list sees them.
    /// </summary>
    internal static readonly string[] Built =
    [
        "overflowBackdropCard", "cardPadder-overflowBackdrop", "overflowSquareCard", "cardPadder-overflowSquare",
        "backdropCard", "cardPadder-backdrop", "squareCard", "cardPadder-square"
    ];

    private readonly IServerApplicationPaths _paths;
    private readonly ILogger<WebClientCheck> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebClientCheck"/> class.
    /// </summary>
    /// <param name="paths">The server's paths, for the web client's folder.</param>
    /// <param name="logger">Writes what the check finds.</param>
    public WebClientCheck(IServerApplicationPaths paths, ILogger<WebClientCheck> logger)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets the request page's markup, with its script. The web client markup it builds with is Stolon's, which Stolon's
    /// check covers itself.
    /// </summary>
    /// <returns>The page.</returns>
    internal static string PageMarkup() => EmbeddedResources.Read("necto_requests_page.html");

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Off the startup path, since it reads every stylesheet the web client has.
        _ = Siphonophore.Stolon.WebClientCheck.RunAsync(_paths, _logger, "request page", PageMarkup(), Built, checkDetailsTemplate: true, CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
