using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Siphonophore.Stolon;
using MediaBrowser.Controller;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Necto.Services.Requests;

/// <summary>
/// Adds a Requests link to the web client's menu (menuLinks in its config.json) the first time the plugin starts,
/// unless a link to the request page is already there. It runs once: a link removed afterwards stays removed. An
/// Issues link is added the same way each time issues are turned on, and taken out when they are turned off.
/// </summary>
[PluginService(ServiceLifetime.Singleton, ServiceType = typeof(IHostedService))]
public sealed class MenuLinkInstaller : IHostedService, IDisposable
{
    /// <summary>Where the link goes, from the web client at /web/: the request page beside it.</summary>
    internal const string LinkUrl = "../requests/";

    /// <summary>Where the Issues link goes, from the web client at /web/: the issue page beside it.</summary>
    internal const string IssuesLinkUrl = "../issues/";

    // One change to the Issues link at a time, as settings saved quickly one after another each start one.
    private readonly SemaphoreSlim _issuesLink = new(1, 1);

    private static readonly JsonSerializerOptions _indented = new() { WriteIndented = true };

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
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (RequestConfig.Instance is not { } plugin || Plugin.Instance is not { } loaded)
        {
            return;
        }

        loaded.ConfigurationChanged += OnConfigurationChanged;
        await AddRequestsLinkAsync(plugin, cancellationToken).ConfigureAwait(false);
        await SyncIssuesLinkAsync(cancellationToken).ConfigureAwait(false);
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

    /// <inheritdoc />
    public void Dispose() => _issuesLink.Dispose();

    // Settings saved: the Issues link follows the Issues setting, off the saving thread, which holds the settings' lock.
    private void OnConfigurationChanged(object? sender, MediaBrowser.Model.Plugins.BasePluginConfiguration e)
        => _ = Task.Run(() => SyncIssuesLinkAsync(CancellationToken.None));

    // The Requests link, added once.
    private async Task AddRequestsLinkAsync(RequestConfig plugin, CancellationToken cancellationToken)
    {
        if (plugin.ReadConfiguration(c => c.MenuLinkAdded))
        {
            return;
        }

        var path = string.IsNullOrEmpty(_paths.WebPath) ? null : Path.Combine(_paths.WebPath, "config.json");
        try
        {
            // Nothing was added either way, so it is tried again next start rather than marked done.
            if (path is null || !File.Exists(path))
            {
                _logger.LogInformation("The web client's config.json was not found, so no Requests menu link was added");
                return;
            }

            if (JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)) is not JsonObject web)
            {
                _logger.LogWarning("The web client's config.json at {Path} is not a JSON object, so no Requests menu link was added", path);
                return;
            }

            if (AddLink(web))
            {
                await WriteAsync(path, web.ToJsonString(_indented), cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Added a Requests link to the web client's menu in {Path}", path);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Anything at all, such as a config.json with a key twice, which browsers accept but this doesn't:
            // a menu link is never worth stopping the server for.
            // Left to try again next start; the administrator can add the link by hand meanwhile.
            _logger.LogWarning(ex, "Could not add a Requests link to the web client's menu in {Path}; add {Url} to its menuLinks to show one", path, LinkUrl);
            return;
        }

        plugin.MutateConfiguration(c =>
        {
            c.MenuLinkAdded = true;
            return true;
        });
    }

    /// <summary>
    /// Puts the Issues link in the web client's menu when issues are on and the plugin has not added it since they were
    /// turned on, and takes it out when they are off. Anything that fails is tried again at the next start or save.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task.</returns>
    internal async Task SyncIssuesLinkAsync(CancellationToken cancellationToken)
    {
        if (RequestConfig.Instance is not { } plugin)
        {
            return;
        }

        await _issuesLink.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var (on, added) = plugin.ReadIssues(c => (c.Enabled, c.MenuLinkAdded));
            if (on == added)
            {
                return;
            }

            var path = string.IsNullOrEmpty(_paths.WebPath) ? null : Path.Combine(_paths.WebPath, "config.json");
            if (path is null || !File.Exists(path)
                || JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)) is not JsonObject web)
            {
                _logger.LogInformation("The web client's config.json was not found or read, so the Issues menu link was not changed");
                return;
            }

            if (on ? AddIssuesLink(web) : RemoveIssuesLink(web))
            {
                await WriteAsync(path, web.ToJsonString(_indented), cancellationToken).ConfigureAwait(false);
                if (on)
                {
                    _logger.LogInformation("Added an Issues link to the web client's menu in {Path}", path);
                }
                else
                {
                    _logger.LogInformation("Took the Issues link out of the web client's menu in {Path}", path);
                }
            }

            plugin.MutateAll(c =>
            {
                c.Issues ??= new();
                c.Issues.MenuLinkAdded = on;
                return true;
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A menu link is never worth more than a warning; it's tried again at the next start or save.
            _logger.LogWarning(ex, "Could not change the Issues link in the web client's menu; add or remove {Url} in its menuLinks by hand", IssuesLinkUrl);
        }
        finally
        {
            _issuesLink.Release();
        }
    }

    /// <summary>Adds the Issues link to a web client config's menuLinks, unless one already goes to the issue page.</summary>
    /// <param name="web">The web client's config.json.</param>
    /// <returns>Whether it was added.</returns>
    internal static bool AddIssuesLink(JsonObject web)
    {
        ArgumentNullException.ThrowIfNull(web);
        if (web["menuLinks"] is not JsonArray links)
        {
            links = new JsonArray();
            web["menuLinks"] = links;
        }

        if (links.OfType<JsonObject>().Any(l => GoesTo(UrlOf(l), "issues")))
        {
            return false;
        }

        links.Add(new JsonObject { ["name"] = "Issues", ["icon"] = "bug_report", ["url"] = IssuesLinkUrl });
        return true;
    }

    /// <summary>Takes every link to the issue page out of a web client config's menuLinks.</summary>
    /// <param name="web">The web client's config.json.</param>
    /// <returns>Whether any was taken out.</returns>
    internal static bool RemoveIssuesLink(JsonObject web)
    {
        ArgumentNullException.ThrowIfNull(web);
        if (web["menuLinks"] is not JsonArray links)
        {
            return false;
        }

        var gone = links.OfType<JsonObject>().Where(l => GoesTo(UrlOf(l), "issues")).ToList();
        foreach (var link in gone)
        {
            links.Remove(link);
        }

        return gone.Count > 0;
    }

    private static string? UrlOf(JsonObject link) => link["url"]?.GetValueKind() == JsonValueKind.String ? link["url"]!.GetValue<string>() : null;

    // Written beside it and swapped in, so the web client never reads half a file. Where its folder can't be written,
    // as in Docker images where config.json is mounted on its own, the file itself is written instead.
    internal static async Task WriteAsync(string path, string json, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            await AtomicFile.WriteAsync(path, (file, ct) => file.WriteAsync(bytes, ct).AsTask(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // A file mounted on its own can be written but not replaced.
            await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Adds the Requests link to a web client config's menuLinks, unless one already goes to the request page.
    /// </summary>
    /// <param name="web">The web client's config.json.</param>
    /// <returns>Whether it was added.</returns>
    internal static bool AddLink(JsonObject web)
    {
        ArgumentNullException.ThrowIfNull(web);
        if (web["menuLinks"] is not JsonArray links)
        {
            links = new JsonArray();
            web["menuLinks"] = links;
        }

        if (links.OfType<JsonObject>().Any(l => GoesToRequests(UrlOf(l))))
        {
            return false;
        }

        links.Add(new JsonObject { ["name"] = "Requests", ["icon"] = "search", ["url"] = LinkUrl });
        return true;
    }

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
}
