using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Configuration.Requests;

/// <summary>
/// A Radarr (movies) or Sonarr (shows) server approved requests are sent to.
/// </summary>
public class ArrServer
{
    /// <summary>Gets or sets the server's id, which keeps its stored key when the page saves it back.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

    /// <summary>Gets or sets the server's name, such as Radarr or 4K Radarr.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets what the server takes: movies for Radarr, shows for Sonarr.</summary>
    public RequestType Type { get; set; }

    /// <summary>Gets or sets the address, such as http://localhost:7878.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Gets or sets the API key, encrypted at rest.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this is its type's default server: the one titles are looked up on,
    /// Discover reads from, and new requests' profile choices start with. With none marked, the first is the default.
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the server starts searching for a title as soon as a request is sent to
    /// it. Off, the title is only added (or monitored) and waits for the server's own RSS sync or a search started there.
    /// </summary>
    public bool SearchOnAdd { get; set; } = true;

    /// <summary>
    /// Gets or sets the address administrators open the server at in a browser, when it isn't the one the plugin
    /// talks to (a reverse proxy, say). Empty uses the address.
    /// </summary>
    public string ExternalUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the ways titles can be added to it, the first being its default.</summary>
    public List<ArrProfileOption> Profiles { get; set; } = new();
}

/// <summary>
/// When Radarr considers a movie available to grab: Radarr's own minimumAvailability values.
/// </summary>
public enum ArrMinimumAvailability
{
    /// <summary>As soon as it's announced.</summary>
    Announced,

    /// <summary>Once it's in cinemas.</summary>
    InCinemas,

    /// <summary>Once it's out on disc or digitally.</summary>
    Released
}

/// <summary>
/// How Sonarr numbers and searches a show's episodes: Sonarr's own seriesType values.
/// </summary>
public enum ArrSeriesType
{
    /// <summary>Season and episode numbers.</summary>
    Standard,

    /// <summary>Episodes named by air date, such as talk shows.</summary>
    Daily,

    /// <summary>Absolute episode numbers, as anime releases use.</summary>
    Anime
}

/// <summary>
/// One way of adding titles to a server: a named quality profile and root folder, such as 1080p or 4K.
/// </summary>
public class ArrProfileOption
{
    /// <summary>Gets or sets the profile's id, which requests and permissions refer to.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

    /// <summary>Gets or sets the profile's name, such as 1080p or 4K.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the server's quality profile titles are added with.</summary>
    public int QualityProfileId { get; set; }

    /// <summary>Gets or sets the server's root folder titles are added to.</summary>
    public string RootFolder { get; set; } = string.Empty;

    /// <summary>Gets or sets the ids of the server's own tags titles added with this profile get.</summary>
    public List<int> Tags { get; set; } = new();

    /// <summary>Gets or sets, for Radarr, when a movie added with this profile counts as available to grab.</summary>
    public ArrMinimumAvailability MinimumAvailability { get; set; } = ArrMinimumAvailability.Released;

    /// <summary>Gets or sets, for Sonarr, the series type shows are added as.</summary>
    public ArrSeriesType SeriesType { get; set; } = ArrSeriesType.Standard;

    /// <summary>Gets or sets, for Sonarr, whether a show's episodes go in a folder per season.</summary>
    public bool SeasonFolder { get; set; } = true;
}

/// <summary>
/// Finds the server and profile a request goes to.
/// </summary>
public static class ArrServers
{
    /// <summary>
    /// Gets the profiles that can take a type of request, with their servers, in order: the default server's first,
    /// then the others in the order they're listed, each server's own profiles first to last.
    /// </summary>
    /// <param name="config">The configuration.</param>
    /// <param name="type">The request type.</param>
    /// <param name="defaultFirst">False to keep the listed order, for requests sent before servers were recorded, which went to the first.</param>
    /// <returns>The usable profiles.</returns>
    internal static IEnumerable<(ArrServer Server, ArrProfileOption Profile)> Usable(RequestSettings config, RequestType type, bool defaultFirst = true)
    {
        var servers = (config.ArrServers ?? new())
            .Where(s => s is not null && s.Type == type && !string.IsNullOrWhiteSpace(s.Url) && !string.IsNullOrEmpty(s.ApiKey));

        // OrderBy keeps the listed order among equals, so with no default marked (an older configuration) the first stays first.
        return (defaultFirst ? servers.OrderBy(s => s.IsDefault ? 0 : 1) : servers)
            .SelectMany(s => (s.Profiles ?? new()).Where(p => p is not null && p.QualityProfileId > 0 && !string.IsNullOrWhiteSpace(p.RootFolder)).Select(p => (s, p)));
    }

    /// <summary>
    /// Tidies the servers as a save takes them: one default per type (the first marked, or else the first listed), a
    /// browser address that isn't http or https dropped, and each profile's settings within what the server accepts.
    /// Keys, ids and names are the plugin's business when it saves.
    /// </summary>
    /// <param name="servers">The servers, changed in place.</param>
    internal static void Normalize(List<ArrServer> servers)
    {
        ArgumentNullException.ThrowIfNull(servers);
        foreach (var group in servers.Where(s => s is not null).GroupBy(s => s.Type))
        {
            var chosen = group.FirstOrDefault(s => s.IsDefault) ?? group.First();
            foreach (var server in group)
            {
                server.IsDefault = ReferenceEquals(server, chosen);
            }
        }

        foreach (var server in servers.Where(s => s is not null))
        {
            server.ExternalUrl = ArrClient.TryNormalizeUrl(server.ExternalUrl, out var external) ? external : string.Empty;
            foreach (var profile in (server.Profiles ?? new()).Where(p => p is not null))
            {
                // Tags are the server's own ids, so only positive ones, each once; a server has a handful, not hundreds.
                profile.Tags = (profile.Tags ?? new()).Where(t => t > 0).Distinct().Take(100).ToList();
                if (!Enum.IsDefined(profile.MinimumAvailability))
                {
                    profile.MinimumAvailability = ArrMinimumAvailability.Released;
                }

                if (!Enum.IsDefined(profile.SeriesType))
                {
                    profile.SeriesType = ArrSeriesType.Standard;
                }
            }
        }
    }

    /// <summary>
    /// Finds a profile by id among those that can take a type of request, or else the default server's first. Null
    /// when no server is set up for that type.
    /// </summary>
    /// <param name="config">The configuration.</param>
    /// <param name="type">The request type.</param>
    /// <param name="profileId">The profile wanted, or empty for the default.</param>
    /// <param name="defaultFirst">False to fall back to the first listed server rather than the default.</param>
    /// <returns>The server and profile, or null.</returns>
    internal static (ArrServer Server, ArrProfileOption Profile)? Resolve(RequestSettings config, RequestType type, string? profileId, bool defaultFirst = true)
    {
        var usable = Usable(config, type, defaultFirst).ToList();
        if (usable.Count == 0)
        {
            return null;
        }

        var wanted = string.IsNullOrEmpty(profileId) ? -1 : usable.FindIndex(u => string.Equals(u.Profile.Id, profileId, StringComparison.Ordinal));
        return usable[wanted >= 0 ? wanted : 0];
    }
}
