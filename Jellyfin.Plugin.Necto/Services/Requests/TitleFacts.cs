using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests;

/// <summary>
/// What the request rules need to know about a title: its genres, age rating and whether it is out.
/// </summary>
/// <param name="Genres">The genres.</param>
/// <param name="Rating">The age rating, when known.</param>
/// <param name="Released">Whether it is out, or null when unknown.</param>
/// <param name="Known">Whether the title's details could be read at all; when not, the rest is empty rather than true.</param>
internal sealed record TitleFacts(List<string> Genres, string? Rating, bool? Released, bool Known)
{
    /// <summary>Gets the name the metadata providers know the title by, when they were asked.</summary>
    public string? Name { get; init; }

    /// <summary>Gets its TMDb keywords, original language and collection, when TMDb was asked.</summary>
    public TitleTraits? Traits { get; init; }
}

/// <summary>What TMDb says of a title beyond its genres, for the blocklist: its keywords, original language and collection.</summary>
/// <param name="Keywords">Its TMDb keyword ids.</param>
/// <param name="Language">Its original language, as an ISO 639-1 code.</param>
/// <param name="Collection">The TMDb collection a movie belongs to.</param>
public sealed record TitleTraits(IReadOnlyCollection<int> Keywords, string? Language, int? Collection);
