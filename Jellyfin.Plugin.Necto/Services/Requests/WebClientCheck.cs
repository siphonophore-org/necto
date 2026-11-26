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
    /// The web client's classes the page's script builds from parts, such as <c>'cardPadder-' + shape</c>, which reading
    /// its markup cannot see.
    /// </summary>
    internal static readonly string[] Built =
    [
        "overflowPortraitCard", "portraitCard", "cardPadder-overflowPortrait", "cardPadder-portrait",
        "overflowBackdropCard", "cardPadder-overflowBackdrop", "overflowSquareCard", "cardPadder-overflowSquare",
        "backdropCard", "cardPadder-backdrop", "squareCard", "cardPadder-square",
        "defaultCardBackground1", "defaultCardBackground2", "defaultCardBackground3", "defaultCardBackground4", "defaultCardBackground5",
        "button-submit", "button-delete", "mediaInfoCriticRatingFresh", "mediaInfoCriticRatingRotten"
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
    /// Gets everything the request page's markup is built from: the page, and its script that builds the web client's
    /// markup, read as a script of the page so its class lists count.
    /// </summary>
    /// <returns>The page with the script inside it.</returns>
    internal static string PageMarkup()
        => EmbeddedResources.Read("necto_requests_page.html") + "\n<script>\n" + EmbeddedResources.Read("necto_requests_webclient.js") + "\n</script>\n";

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
