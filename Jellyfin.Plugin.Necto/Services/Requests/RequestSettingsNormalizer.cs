using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Requests;

/// <summary>
/// Cleans the Requests and Issues settings of a configuration before it is saved, however the save came in. Keys keep
/// what is stored for the kept sentinel, numbers stay in range, and what only Necto changes keeps its stored value.
/// </summary>
public static class RequestSettingsNormalizer
{
    /// <summary>
    /// Cleans the Requests and Issues settings of a configuration about to be saved.
    /// </summary>
    /// <param name="incoming">The configuration about to be saved.</param>
    /// <param name="stored">The configuration in effect.</param>
    /// <param name="secrets">The protector the keys are stored with.</param>
    public static void Normalize(PluginConfiguration incoming, PluginConfiguration stored, SecretProtector secrets)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(secrets);
        incoming.Requests = Requests(incoming.Requests ?? new RequestSettings(), stored.Requests ?? new RequestSettings(), secrets);
        incoming.Issues = Issues(incoming.Issues ?? new IssueSettings(), stored.Issues ?? new IssueSettings());
    }

    /// <summary>
    /// Cleans the Issues settings.
    /// </summary>
    /// <param name="incoming">The settings about to be saved.</param>
    /// <param name="stored">The settings in effect.</param>
    /// <returns>The cleaned settings.</returns>
    public static IssueSettings Issues(IssueSettings incoming, IssueSettings stored)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(stored);
        incoming.RetentionDays = Math.Clamp(incoming.RetentionDays, 0, 3650);
        incoming.LimitDays = Math.Clamp(incoming.LimitDays, 1, 365);

        // Set by Necto once the menu link is in or out, and by Menu links. A page saving its copy never changes them.
        incoming.MenuLinkAdded = stored.MenuLinkAdded;
        incoming.MenuLinkName = stored.MenuLinkName;
        incoming.MenuLinkIcon = stored.MenuLinkIcon;
        return incoming;
    }

    /// <summary>
    /// Cleans the Requests settings.
    /// </summary>
    /// <param name="incoming">The settings about to be saved.</param>
    /// <param name="stored">The settings in effect.</param>
    /// <param name="secrets">The protector the keys are stored with.</param>
    /// <returns>The cleaned settings.</returns>
    public static RequestSettings Requests(RequestSettings incoming, RequestSettings stored, SecretProtector secrets)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(secrets);

        // The settings page never receives the keys. It posts the kept sentinel for an untouched one, and each server
        // keeps its stored key by id, only while its address is unchanged, so a key never follows a server to a new address.
        var storedServers = (stored.ArrServers ?? new()).Where(s => s is not null).GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        // A profile id names one profile across every server, so a request can only ever resolve to one.
        var profileIds = new HashSet<string>(StringComparer.Ordinal);

        incoming.TmdbApiKey = Resolve(incoming.TmdbApiKey, stored.TmdbApiKey, secrets);
        incoming.ArrServers = (incoming.ArrServers ?? new())
            .Where(s => s is not null && s.Type is RequestType.Movie or RequestType.Series)
            .Select(s =>
            {
                s.Id = CleanId(s.Id);
                s.Name = Clip(s.Name, 100);
                s.Url = (s.Url ?? string.Empty).Trim();
                s.ExternalUrl = Clip(s.ExternalUrl, 500);
                var before = storedServers.GetValueOrDefault(s.Id);
                s.ApiKey = Resolve(s.ApiKey, before is not null && ArrClient.SameAddress(before.Url, s.Url) ? before.ApiKey : null, secrets);
                s.Profiles = (s.Profiles ?? new())
                    .Where(p => p is not null)
                    .Select(p =>
                    {
                        p.Id = CleanId(p.Id);
                        if (!profileIds.Add(p.Id))
                        {
                            p.Id = CleanId(null);
                            profileIds.Add(p.Id);
                        }

                        p.Name = Clip(p.Name, 100);
                        p.RootFolder = (p.RootFolder ?? string.Empty).Trim();
                        return p;
                    })
                    .Take(50)
                    .ToList();
                return s;
            })
            .GroupBy(s => s.Id)
            .Select(g => g.First())
            .Take(20)
            .ToList();

        // One default server per type, a browser address only when it is http or https, and each profile's Radarr and
        // Sonarr settings within what they accept.
        ArrServers.Normalize(incoming.ArrServers);

        incoming.RetentionDays = Math.Clamp(incoming.RetentionDays, 0, 3650);
        incoming.MovieQuotaDays = Math.Clamp(incoming.MovieQuotaDays, 1, 365);
        incoming.SeriesQuotaDays = Math.Clamp(incoming.SeriesQuotaDays, 1, 365);
        incoming.AutoDeclineAfterDays = Math.Clamp(incoming.AutoDeclineAfterDays, 0, 3650);
        incoming.AgingAfterDays = Math.Clamp(incoming.AgingAfterDays, 0, 3650);
        if (!Enum.IsDefined(incoming.RefreshOnAvailable))
        {
            incoming.RefreshOnAvailable = LibraryRefresh.ItemFolder;
        }

        incoming.AutoDeclineReason = Clip(incoming.AutoDeclineReason, 500);
        incoming.AutoApproveRatings = Words(incoming.AutoApproveRatings);
        incoming.BlockedGenres = Words(incoming.BlockedGenres);
        incoming.BlockedRatings = Words(incoming.BlockedRatings);
        incoming.BlockedKeywords = Words(incoming.BlockedKeywords);
        incoming.BlockedTags = Named(incoming.BlockedTags);
        incoming.BlockedCollections = Named(incoming.BlockedCollections);
        incoming.BlockedLanguages = (incoming.BlockedLanguages ?? new())
            .Select(l => (l ?? string.Empty).Trim().ToLowerInvariant()).Where(l => l.Length is 2 or 3 && l.All(char.IsAsciiLetterLower)).Distinct().Take(100).ToList();

        // Home rows: a heading, a kind and at least one id each, and an id of their own for their address.
        incoming.HomeRows = (incoming.HomeRows ?? new())
            .Where(r => r is not null && Enum.IsDefined(r.Type) && HomeRow.Kinds.Contains(r.Kind)
                && !(r.Kind == "Network" && r.Type != RequestType.Series) && !string.IsNullOrWhiteSpace(r.Title))
            .Select(r =>
            {
                r.Title = Clip(r.Title, 60);
                r.Ids = string.Join(',', (r.Ids ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(i => i.Length <= 10 && i.All(char.IsAsciiDigit)).Distinct().Take(10));
                r.Sort = TitleFilter.Sorts.Contains(r.Sort) ? r.Sort : string.Empty;
                r.Names = Clip(r.Names, 1000);
                r.Id = r.Id is { Length: > 0 and <= 32 } id && id.All(char.IsAsciiLetterOrDigit) ? id : Guid.NewGuid().ToString("N")[..12];
                return r;
            })
            .Where(r => r.Ids.Length > 0)
            .DistinctBy(r => r.Id)
            .Take(20)
            .ToList();

        // The rows' order and the built-in ones hidden, by key: only keys a row has.
        var keys = SearchService.DiscoverKinds.Select(k => k.Key).Concat(incoming.HomeRows.Select(r => SearchService.CustomRowPrefix + r.Id)).ToHashSet(StringComparer.Ordinal);
        incoming.HomeRowOrder = (incoming.HomeRowOrder ?? new()).Where(k => k is not null && keys.Contains(k)).Distinct().ToList();
        incoming.HiddenHomeRows = (incoming.HiddenHomeRows ?? new()).Where(k => k is not null && SearchService.DiscoverKinds.Any(d => d.Key == k)).Distinct().ToList();

        // Blocked titles change only by blocking and unblocking, never by a page saving its copy of them, which may be
        // older than a block made since. The menu link flag is set by Necto alone.
        incoming.BlockedTitles = (stored.BlockedTitles ?? new())
            .Where(t => t is not null && Enum.IsDefined(t.Type) && (t.Tmdb ?? t.Tvdb ?? t.Imdb) is not null)
            .Take(5000)
            .ToList();
        // Menu links sets the link's name and icon through its own route.
        incoming.MenuLinkAdded = stored.MenuLinkAdded;
        incoming.MenuLinkName = stored.MenuLinkName;
        incoming.MenuLinkIcon = stored.MenuLinkIcon;
        return incoming;
    }

    // A secret from a page. The kept sentinel keeps what is stored, and anything else is new. One already encrypted is
    // only taken back as the value stored here, so an encrypted key copied from elsewhere cannot move onto another server.
    private static string Resolve(string? value, string? stored, SecretProtector secrets)
    {
        value = value?.Trim();
        return SecretProtector.IsProtected(value) && !string.Equals(value, stored, StringComparison.Ordinal)
            ? string.Empty
            : secrets.ResolveIncoming(value, stored);
    }

    // An id of letters and digits, or a new one.
    private static string CleanId(string? id)
        => !string.IsNullOrEmpty(id) && id.Length <= 64 && id.All(char.IsAsciiLetterOrDigit) ? id : Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

    // A list of short words or phrases, trimmed, without blanks or repeats.
    private static List<string> Words(List<string>? words)
        => (words ?? new()).Select(w => (w ?? string.Empty).Trim()).Where(w => w.Length is > 0 and <= 100)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(500).ToList();

    // TMDb keywords or collections, one of each id, with its name.
    private static List<TmdbName> Named(List<TmdbName>? names)
        => (names ?? new()).Where(n => n is not null && n.Id > 0).Select(n => new TmdbName { Id = n.Id, Name = Clip(n.Name, 200) }).DistinctBy(n => n.Id).Take(500).ToList();

    private static string Clip(string? text, int max)
    {
        text = (text ?? string.Empty).Trim();
        return text.Length > max ? text[..max] : text;
    }
}
