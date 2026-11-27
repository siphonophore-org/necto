using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Services.Requests;
using MediaBrowser.Controller;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.MenuLinks;

/// <summary>
/// The Menu links editor's side: reads the web client's menu links, marks the ones Requests and Issues keep, and saves
/// the list the editor sends.
/// </summary>
[PluginService(ServiceLifetime.Singleton)]
public sealed class MenuLinksService
{
    private readonly IServerApplicationPaths _paths;
    private readonly ILogger<MenuLinksService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MenuLinksService"/> class.
    /// </summary>
    /// <param name="paths">The server's paths, for the web client's folder.</param>
    /// <param name="logger">The logger.</param>
    public MenuLinksService(IServerApplicationPaths paths, ILogger<MenuLinksService> logger)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Marks each link that a tool keeps while it is on: the link to the request page, and to the issue page.</summary>
    /// <param name="links">The links.</param>
    /// <param name="requestsOn">Whether Requests is on.</param>
    /// <param name="issuesOn">Whether Issues is on.</param>
    public static void MarkManaged(IEnumerable<MenuLink> links, bool requestsOn, bool issuesOn)
    {
        ArgumentNullException.ThrowIfNull(links);
        foreach (var link in links)
        {
            link.Managed = requestsOn && MenuLinkInstaller.GoesTo(link.Url, "requests") ? ToolSwitches.Requests
                : issuesOn && MenuLinkInstaller.GoesTo(link.Url, "issues") ? ToolSwitches.Issues
                : null;
        }
    }

    /// <summary>
    /// Gives the translation key of why a new list cannot replace the old one, or null when it can. Each tool that is
    /// on keeps a link to its page when the old list had one.
    /// </summary>
    /// <param name="before">The links in the file.</param>
    /// <param name="after">The links the editor sent.</param>
    /// <param name="requestsOn">Whether Requests is on.</param>
    /// <param name="issuesOn">Whether Issues is on.</param>
    /// <returns>The key, or null.</returns>
    public static string? KeepsManaged(IReadOnlyList<MenuLink> before, IReadOnlyList<MenuLink> after, bool requestsOn, bool issuesOn)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        bool Has(IReadOnlyList<MenuLink> list, string page) => list.Any(l => MenuLinkInstaller.GoesTo(l.Url, page));
        if (requestsOn && Has(before, "requests") && !Has(after, "requests"))
        {
            return TranslationKeys.MenuLinksErrorKeepRequests;
        }

        return issuesOn && Has(before, "issues") && !Has(after, "issues") ? TranslationKeys.MenuLinksErrorKeepIssues : null;
    }

    /// <summary>Reads the links, with whether they can be saved.</summary>
    /// <param name="language">The language of the problem sentence.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The links and the file's state.</returns>
    public async Task<MenuLinksView> ReadAsync(string? language, CancellationToken cancellationToken)
    {
        var path = MenuLinksFile.PathOf(_paths);
        var view = new MenuLinksView { Path = path ?? string.Empty };
        if (path is null || !File.Exists(path))
        {
            view.Problem = StolonTranslations.Get(TranslationKeys.MenuLinksProblemMissing, language);
            return view;
        }

        try
        {
            if (await MenuLinksFile.ReadAsync(path, cancellationToken).ConfigureAwait(false) is not { } web)
            {
                view.Problem = StolonTranslations.Get(TranslationKeys.MenuLinksProblemNotJson, language);
                return view;
            }

            view.Links = MenuLinksFile.LinksOf(web);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not read the web client's menu links in {Path}", path);
            view.Problem = StolonTranslations.Get(ex is JsonException ? TranslationKeys.MenuLinksProblemNotJson : TranslationKeys.MenuLinksProblemUnreadable, language);
            return view;
        }

        var (requestsOn, issuesOn) = TogglesOn();
        MarkManaged(view.Links, requestsOn, issuesOn);
        view.Writable = MenuLinksFile.CanWrite(path);
        if (!view.Writable)
        {
            view.Problem = StolonTranslations.Get(TranslationKeys.MenuLinksProblemReadOnly, language);
        }

        return view;
    }

    /// <summary>
    /// Saves the links in their order, keeping every other key of config.json, and remembers the name and icon of a
    /// link Requests or Issues keeps.
    /// </summary>
    /// <param name="links">The links.</param>
    /// <param name="language">The language of a refusal.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The links as saved, or the reason they were not.</returns>
    public async Task<(MenuLinksView? View, string? Error)> SaveAsync(IReadOnlyList<MenuLink>? links, string? language, CancellationToken cancellationToken)
    {
        if (links is null)
        {
            return (null, StolonTranslations.Get(TranslationKeys.ErrorNothingSent, language));
        }

        if (links.Count > MenuLinksFile.MaxLinks)
        {
            return (null, StolonTranslations.Format(TranslationKeys.MenuLinksErrorTooMany, language, MenuLinksFile.MaxLinks));
        }

        if (links.Select(MenuLinksFile.ProblemOf).FirstOrDefault(p => p is not null) is { } problem)
        {
            return (null, StolonTranslations.Get(problem, language));
        }

        var path = MenuLinksFile.PathOf(_paths);
        if (path is null || !File.Exists(path))
        {
            return (null, StolonTranslations.Get(TranslationKeys.MenuLinksProblemMissing, language));
        }

        var (requestsOn, issuesOn) = TogglesOn();
        await MenuLinksFile.Lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (await MenuLinksFile.ReadAsync(path, cancellationToken).ConfigureAwait(false) is not { } web)
            {
                return (null, StolonTranslations.Get(TranslationKeys.MenuLinksProblemNotJson, language));
            }

            if (KeepsManaged(MenuLinksFile.LinksOf(web), links, requestsOn, issuesOn) is { } keep)
            {
                return (null, StolonTranslations.Get(keep, language));
            }

            MenuLinksFile.SetLinks(web, links);
            await MenuLinksFile.WriteAsync(path, web, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return (null, StolonTranslations.Get(TranslationKeys.MenuLinksProblemNotJson, language));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not save the web client's menu links in {Path}", path);
            return (null, StolonTranslations.Get(TranslationKeys.MenuLinksProblemReadOnly, language));
        }
        finally
        {
            MenuLinksFile.Lock.Release();
        }

        Remember(links, requestsOn, issuesOn);
        _logger.LogInformation("Saved {Count} menu links in {Path}", links.Count, path);
        return (await ReadAsync(language, cancellationToken).ConfigureAwait(false), null);
    }

    private static (bool RequestsOn, bool IssuesOn) TogglesOn()
        => Plugin.Instance?.ReadConfiguration(c => (ToolSwitches.IsOn(c, ToolSwitches.Requests), ToolSwitches.IsOn(c, ToolSwitches.Issues))) ?? (false, false);

    // The name and icon of the links Requests and Issues keep, for when Necto adds them again after a tool is turned
    // off and on.
    private static void Remember(IReadOnlyList<MenuLink> links, bool requestsOn, bool issuesOn)
    {
        var requests = requestsOn ? links.FirstOrDefault(l => MenuLinkInstaller.GoesTo(l.Url, "requests")) : null;
        var issues = issuesOn ? links.FirstOrDefault(l => MenuLinkInstaller.GoesTo(l.Url, "issues")) : null;
        if ((requests is null && issues is null) || Plugin.Instance is not { } plugin)
        {
            return;
        }

        plugin.MutateConfiguration(c =>
        {
            var changed = false;
            if (requests is not null && c.Requests is { } r && (r.MenuLinkName != requests.Name.Trim() || r.MenuLinkIcon != requests.Icon.Trim()))
            {
                r.MenuLinkName = requests.Name.Trim();
                r.MenuLinkIcon = requests.Icon.Trim();
                changed = true;
            }

            if (issues is not null && c.Issues is { } i && (i.MenuLinkName != issues.Name.Trim() || i.MenuLinkIcon != issues.Icon.Trim()))
            {
                i.MenuLinkName = issues.Name.Trim();
                i.MenuLinkIcon = issues.Icon.Trim();
                changed = true;
            }

            return changed;
        });
    }
}
