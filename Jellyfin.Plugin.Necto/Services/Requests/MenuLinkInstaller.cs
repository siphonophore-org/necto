using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Siphonophore.Stolon;
using MediaBrowser.Controller;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Services.MenuLinks;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Necto.Services.Requests;

/// <summary>
/// Keeps the Requests and Issues links in the web client's menu (menuLinks in its config.json) in step with their
/// switches. Each link is added once when its tool is on, with the name and icon set in Menu links, unless a link to
/// its page is there already, so one removed by hand stays removed. Each is taken out when its tool is turned off.
/// </summary>
[PluginService(ServiceLifetime.Singleton, ServiceType = typeof(IHostedService))]
public sealed class MenuLinkInstaller : IHostedService
{
    /// <summary>Where the link goes, from the web client at /web/: the request page beside it.</summary>
    internal const string LinkUrl = "../requests/";

    /// <summary>Where the Issues link goes, from the web client at /web/: the issue page beside it.</summary>
    internal const string IssuesLinkUrl = "../issues/";

    private readonly IServerApplicationPaths _paths;
    private readonly ILogger<MenuLinkInstaller> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MenuLinkInstaller"/> class.
    /// </summary>
    /// <param name="paths">The server's paths, for the web client's folder.</param>
    /// <param name="logger">The logger.</param>
    public MenuLinkInstaller(IServerApplicationPaths paths, ILogger<MenuLinkInstaller> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (Plugin.Instance is not { } loaded)
        {
            return Task.CompletedTask;
        }

        loaded.ConfigurationChanged += OnConfigurationChanged;
        return SyncAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (Plugin.Instance is { } loaded)
        {
            loaded.ConfigurationChanged -= OnConfigurationChanged;
        }

        return Task.CompletedTask;
    }

    // Settings saved: the links follow the switches, off the saving thread, which holds the settings' lock.
    private void OnConfigurationChanged(object? sender, MediaBrowser.Model.Plugins.BasePluginConfiguration e)
        => _ = Task.Run(() => SyncAsync(CancellationToken.None));

    /// <summary>
    /// Puts the Requests and Issues links in the web client's menu when each tool is on and the plugin has not added it
    /// since it was turned on, and takes each out when its tool is off. Anything that fails is tried again at the next
    /// start or save.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task.</returns>
    internal async Task SyncAsync(CancellationToken cancellationToken)
    {
        if (Plugin.Instance is not { } plugin)
        {
            return;
        }

        await MenuLinksFile.Lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = plugin.ReadConfiguration(c => new
            {
                RequestsOn = ToolSwitches.IsOn(c, ToolSwitches.Requests),
                RequestsAdded = c.Requests?.MenuLinkAdded == true,
                RequestsName = c.Requests?.MenuLinkName,
                RequestsIcon = c.Requests?.MenuLinkIcon,
                IssuesOn = ToolSwitches.IsOn(c, ToolSwitches.Issues),
                IssuesAdded = c.Issues?.MenuLinkAdded == true,
                IssuesName = c.Issues?.MenuLinkName,
                IssuesIcon = c.Issues?.MenuLinkIcon
            });
            if (state.RequestsOn == state.RequestsAdded && state.IssuesOn == state.IssuesAdded)
            {
                return;
            }

            var path = MenuLinksFile.PathOf(_paths);
            if (path is null || !File.Exists(path) || await MenuLinksFile.ReadAsync(path, cancellationToken).ConfigureAwait(false) is not { } web)
            {
                _logger.LogInformation("The web client's config.json was not found or read, so the Requests and Issues menu links were not changed");
                return;
            }

            var changed = false;
            if (state.RequestsOn != state.RequestsAdded)
            {
                changed |= state.RequestsOn ? AddLink(web, state.RequestsName, state.RequestsIcon) : RemoveLink(web);
            }

            if (state.IssuesOn != state.IssuesAdded)
            {
                changed |= state.IssuesOn ? AddIssuesLink(web, state.IssuesName, state.IssuesIcon) : RemoveIssuesLink(web);
            }

            if (changed)
            {
                await MenuLinksFile.WriteAsync(path, web, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Changed the Requests and Issues links in the web client's menu in {Path}", path);
            }

            plugin.MutateConfiguration(c =>
            {
                c.Requests ??= new();
                c.Issues ??= new();
                c.Requests.MenuLinkAdded = state.RequestsOn;
                c.Issues.MenuLinkAdded = state.IssuesOn;
                return true;
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Anything at all, such as a config.json with a key twice, which browsers accept but this does not. A
            // menu link is never worth more than a warning, and it is tried again at the next start or save.
            _logger.LogWarning(ex, "Could not change the Requests and Issues links in the web client's menu; add or remove {Requests} and {Issues} in its menuLinks by hand", LinkUrl, IssuesLinkUrl);
        }
        finally
        {
            MenuLinksFile.Lock.Release();
        }
    }

    /// <summary>Adds the Issues link to a web client config's menuLinks, unless one already goes to the issue page.</summary>
    /// <param name="web">The web client's config.json.</param>
    /// <param name="name">The name set in Menu links, or empty for Issues.</param>
    /// <param name="icon">The icon set in Menu links, or empty for bug_report.</param>
    /// <returns>Whether it was added.</returns>
    internal static bool AddIssuesLink(JsonObject web, string? name = null, string? icon = null)
        => Add(web, "issues", string.IsNullOrWhiteSpace(name) ? "Issues" : name, string.IsNullOrWhiteSpace(icon) ? "bug_report" : icon, IssuesLinkUrl);

    /// <summary>Takes every link to the issue page out of a web client config's menuLinks.</summary>
    /// <param name="web">The web client's config.json.</param>
    /// <returns>Whether any was taken out.</returns>
    internal static bool RemoveIssuesLink(JsonObject web) => Remove(web, "issues");

    /// <summary>Takes every link to the request page out of a web client config's menuLinks.</summary>
    /// <param name="web">The web client's config.json.</param>
    /// <returns>Whether any was taken out.</returns>
    internal static bool RemoveLink(JsonObject web) => Remove(web, "requests");

    // Written beside it and swapped in, so the web client never reads half a file. Where its folder cannot be written,
    // the file itself is written instead.
    internal static Task WriteAsync(string path, string json, CancellationToken cancellationToken)
        => MenuLinksFile.WriteTextAsync(path, json, cancellationToken);

    /// <summary>
    /// Adds the Requests link to a web client config's menuLinks, unless one already goes to the request page.
    /// </summary>
    /// <param name="web">The web client's config.json.</param>
    /// <param name="name">The name set in Menu links, or empty for Requests.</param>
    /// <param name="icon">The icon set in Menu links, or empty for search.</param>
    /// <returns>Whether it was added.</returns>
    internal static bool AddLink(JsonObject web, string? name = null, string? icon = null)
        => Add(web, "requests", string.IsNullOrWhiteSpace(name) ? "Requests" : name, string.IsNullOrWhiteSpace(icon) ? "search" : icon, LinkUrl);

    // A link to this server's request page, however it's written: requests, ../requests/, /requests, or under a base
    // path such as /jellyfin/requests. An address with a host is left out, since it can't be told apart from another
    // site's page of the same name (a Jellyseerr at https://seerr.example.com/requests, say), which isn't this one.
    internal static bool GoesToRequests(string? url) => GoesTo(url, "requests");

    // A link to one of this server's pages beside the web client, by its last part (requests or issues), however it's
    // written, and never an address with a host.
    internal static bool GoesTo(string? url, string page)
    {
        var path = (url ?? string.Empty).Trim().Split('?', '#')[0];
        if (path.StartsWith("//", StringComparison.Ordinal) || (Uri.TryCreate(path, UriKind.Absolute, out var absolute) && !absolute.IsFile))
        {
            return false;
        }

        path = path.TrimEnd('/');
        return path.Equals(page, StringComparison.OrdinalIgnoreCase) || path.EndsWith("/" + page, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Add(JsonObject web, string page, string name, string icon, string url)
    {
        ArgumentNullException.ThrowIfNull(web);
        if (web["menuLinks"] is not JsonArray links)
        {
            links = new JsonArray();
            web["menuLinks"] = links;
        }

        if (links.OfType<JsonObject>().Any(l => GoesTo(UrlOf(l), page)))
        {
            return false;
        }

        links.Add(new JsonObject { ["name"] = name.Trim(), ["icon"] = icon.Trim(), ["url"] = url });
        return true;
    }

    private static bool Remove(JsonObject web, string page)
    {
        ArgumentNullException.ThrowIfNull(web);
        if (web["menuLinks"] is not JsonArray links)
        {
            return false;
        }

        var gone = links.OfType<JsonObject>().Where(l => GoesTo(UrlOf(l), page)).ToList();
        foreach (var link in gone)
        {
            links.Remove(link);
        }

        return gone.Count > 0;
    }

    private static string? UrlOf(JsonObject link) => link["url"]?.GetValueKind() == JsonValueKind.String ? link["url"]!.GetValue<string>() : null;
}
