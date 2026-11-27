using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.CustomPages;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.CustomPages;

/// <summary>
/// Resolves route passwords on save. The page never sees a stored password. It posts back
/// <see cref="SecretProtector.KeptSentinel"/> for a field left alone, and this pairs that field with the route it came
/// from, so the stored value is kept.
/// </summary>
/// <remarks>
/// A route pairs by its page, found by id and then by slug, and its name. A renamed route pairs with a stored route
/// nothing in the incoming settings names, when that route targets the same origin and the pairing is unambiguous. A
/// kept password goes only to a route on the same origin, so a route whose target moved to another host, or a new
/// route given a removed route's name, needs its password entered again. A kept password that pairs with nothing is
/// refused, since saving would strip the credential from a route nobody touched.
/// </remarks>
public static class RouteSecrets
{
    /// <summary>
    /// Replaces every route password with what to store: the stored one where the field kept it, otherwise the new
    /// value encrypted.
    /// </summary>
    /// <param name="incoming">The settings being saved. Their route passwords are rewritten in place.</param>
    /// <param name="stored">The settings in effect.</param>
    /// <param name="secrets">Encrypts the passwords.</param>
    /// <exception cref="ArgumentException">When a kept password cannot be paired with a stored route.</exception>
    public static void Protect(CustomPagesSettings incoming, CustomPagesSettings stored, SecretProtector secrets)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(secrets);

        // Every route pairs before any password changes, so a refused save leaves the settings as they came.
        var pairs = Pair(incoming, stored, null, out var error);
        if (error is not null)
        {
            throw new ArgumentException(error);
        }

        foreach (var (route, match) in pairs)
        {
            var kept = secrets.ResolveIncoming(route.Password, match?.Password);
            route.Password = secrets.Protect(kept);
        }
    }

    /// <summary>
    /// Works out whether <see cref="Protect"/> would refuse the settings, without changing them. The page asks before
    /// it saves, since Jellyfin does not pass a refused save's reason back.
    /// </summary>
    /// <param name="incoming">The settings about to be saved.</param>
    /// <param name="stored">The settings in effect.</param>
    /// <param name="language">The language of the reason, such as de, or <c>null</c> for US English.</param>
    /// <returns>Why the save would be refused, or <c>null</c> when every kept password pairs.</returns>
    public static string? Check(CustomPagesSettings incoming, CustomPagesSettings stored, string? language = null)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(stored);

        Pair(incoming, stored, language, out var error);
        return error;
    }

    /// <summary>
    /// Pairs every incoming route with the stored route its password comes from, or none when it brings its own, and
    /// reports the first kept password that pairs with nothing.
    /// </summary>
    private static List<(PageApiRoute Route, PageApiRoute? Stored)> Pair(
        CustomPagesSettings incoming,
        CustomPagesSettings stored,
        string? language,
        out string? error)
    {
        var pairs = new List<(PageApiRoute Route, PageApiRoute? Stored)>();
        var refused = new List<(int PageIndex, int RouteIndex, string Message)>();
        var claimed = new HashSet<PageApiRoute>(ReferenceEqualityComparer.Instance);
        var pending = new List<(int PageIndex, int RouteIndex, CustomPage Page, PageApiRoute Route)>();
        var incomingPages = incoming.Pages ?? new List<CustomPage>();
        var storedPages = stored.Pages ?? new List<CustomPage>();

        // Every name match claims its stored route first, so a fallback never hands one route's password to another
        // that only sits where it used to.
        for (var p = 0; p < incomingPages.Count; p++)
        {
            var page = incomingPages[p];
            if (page.ApiRoutes is null)
            {
                continue;
            }

            var storedPage = FindPage(storedPages, page);
            for (var r = 0; r < page.ApiRoutes.Count; r++)
            {
                var route = page.ApiRoutes[r];
                var match = storedPage?.ApiRoutes?.FirstOrDefault(s =>
                    string.Equals(s.Name, route.Name, StringComparison.OrdinalIgnoreCase));

                if (match is not null)
                {
                    claimed.Add(match);
                    if (IsKept(route.Password)
                        && !string.IsNullOrEmpty(match.Password)
                        && !SameOrigin(match.Url, route.Url))
                    {
                        refused.Add((p, r, Refusal(route, page, language)));
                    }

                    pairs.Add((route, match));
                }
                else
                {
                    pending.Add((p, r, page, route));
                }
            }
        }

        foreach (var (pageIndex, routeIndex, page, route) in pending)
        {
            var match = IsKept(route.Password)
                ? FindRenamed(incomingPages, storedPages, pageIndex, routeIndex, page, route, claimed)
                : null;

            if (match is not null)
            {
                claimed.Add(match);
            }
            else if (IsKept(route.Password))
            {
                refused.Add((pageIndex, routeIndex, Refusal(route, page, language)));
            }

            pairs.Add((route, match));
        }

        // The first route in page order is named, so the message points at the first one to fix.
        error = refused
            .OrderBy(x => x.PageIndex)
            .ThenBy(x => x.RouteIndex)
            .Select(x => x.Message)
            .FirstOrDefault();
        return pairs;
    }

    private static string Refusal(PageApiRoute route, CustomPage page, string? language)
        => StolonTranslations.Format(TranslationKeys.PagesErrorRoutePassword, language, route.Name, page.Slug);

    private static bool IsKept(string? value)
        => string.Equals(value, SecretProtector.KeptSentinel, StringComparison.Ordinal);

    /// <summary>
    /// Finds the stored page an incoming page is, by its id when it has one, else by its slug.
    /// </summary>
    private static CustomPage? FindPage(List<CustomPage> pages, CustomPage page)
    {
        if (!string.IsNullOrEmpty(page.Id))
        {
            var byId = pages.FirstOrDefault(p => string.Equals(p.Id, page.Id, StringComparison.Ordinal));
            if (byId is not null)
            {
                return byId;
            }
        }

        return FindSlug(pages, page.Slug);
    }

    private static CustomPage? FindSlug(List<CustomPage> pages, string? slug)
        => pages.FirstOrDefault(p => string.Equals(p.Slug, slug, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Finds the stored route a renamed route came from: one nothing incoming names, on the same page or on a stored
    /// page whose slug is gone, aimed at the same origin. A route that kept its name wins, then the one at the same
    /// place while nothing was added or removed around it, then a lone candidate. Anything else pairs with nothing.
    /// </summary>
    private static PageApiRoute? FindRenamed(
        List<CustomPage> incoming,
        List<CustomPage> stored,
        int pageIndex,
        int routeIndex,
        CustomPage page,
        PageApiRoute route,
        HashSet<PageApiRoute> claimed)
    {
        var candidates = new List<(bool Aligned, PageApiRoute Route)>();
        var routeCount = page.ApiRoutes?.Count ?? 0;
        var samePageOf = FindPage(stored, page);
        for (var p = 0; p < stored.Count; p++)
        {
            var storedPage = stored[p];
            var samePage = ReferenceEquals(storedPage, samePageOf);
            if (!samePage && FindSlug(incoming, storedPage.Slug) is not null)
            {
                continue;
            }

            var routes = storedPage.ApiRoutes ?? new List<PageApiRoute>();
            for (var r = 0; r < routes.Count; r++)
            {
                var candidate = routes[r];
                if (!claimed.Contains(candidate)
                    && !string.IsNullOrEmpty(candidate.Password)
                    && SameOrigin(candidate.Url, route.Url))
                {
                    var aligned = r == routeIndex
                        && routes.Count == routeCount
                        && (samePage || (p == pageIndex && stored.Count == incoming.Count));
                    candidates.Add((aligned, candidate));
                }
            }
        }

        var named = candidates
            .Where(c => string.Equals(c.Route.Name, route.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (named.Count > 0)
        {
            return named.Count == 1 ? named[0].Route : null;
        }

        var positioned = candidates.Where(c => c.Aligned).ToList();
        if (positioned.Count == 1)
        {
            return positioned[0].Route;
        }

        return candidates.Count == 1 ? candidates[0].Route : null;
    }

    private static bool SameOrigin(string? left, string? right)
    {
        if (!Uri.TryCreate(left, UriKind.Absolute, out var a) || !Uri.TryCreate(right, UriKind.Absolute, out var b))
        {
            return false;
        }

        return Uri.Compare(a, b, UriComponents.SchemeAndServer, UriFormat.UriEscaped, StringComparison.OrdinalIgnoreCase) == 0;
    }
}
