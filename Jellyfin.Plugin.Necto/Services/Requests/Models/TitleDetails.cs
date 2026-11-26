using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// What a title's details page adds to its search result: tagline, cast and crew.
/// </summary>
public class TitleDetails
{
    /// <summary>Gets or sets the title's name as the library or the metadata providers know it; null when neither could be read.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the tagline.</summary>
    public string? Tagline { get; set; }

    /// <summary>Gets or sets the cast, in billing order.</summary>
    public List<PersonCredit> Cast { get; set; } = new();

    /// <summary>Gets or sets the directors.</summary>
    public List<PersonCredit> Directors { get; set; } = new();

    /// <summary>Gets or sets the writers.</summary>
    public List<PersonCredit> Writers { get; set; } = new();

    /// <summary>Gets or sets the studios or networks.</summary>
    public List<string> Studios { get; set; } = new();

    /// <summary>Gets or sets the genres.</summary>
    public List<string> Genres { get; set; } = new();

    /// <summary>Gets or sets what the title can be browsed by on TMDb (its genres, studios and networks); empty without TMDb.</summary>
    public List<BrowseLink> Browse { get; set; } = new();

    /// <summary>Gets or sets its trailer on YouTube, from TMDb, for a title not in the library; null for none.</summary>
    public string? TrailerUrl { get; set; }

    /// <summary>Gets or sets the collection a movie belongs to on TMDb, such as Dune Collection; null for none.</summary>
    public BrowseLink? Collection { get; set; }

    /// <summary>Gets or sets its TMDb keywords, original language and collection, for the blocklist; not sent to the page.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public TitleTraits? Traits { get; set; }

    /// <summary>Gets or sets the age rating, when known.</summary>
    public string? OfficialRating { get; set; }

    /// <summary>Gets or sets the community rating out of 10, when known.</summary>
    public double? CommunityRating { get; set; }

    /// <summary>Gets or sets the year an ended show last aired, for its run ("2008 - 2013"); null otherwise.</summary>
    public int? EndYear { get; set; }

    /// <summary>Gets or sets when it was first released or aired, when known.</summary>
    public DateTime? ReleaseDate { get; set; }

    /// <summary>
    /// Gets or sets whether the title itself is on the blocklist, for administrators only; null for anyone else.
    /// A title can also be blocked by its genre, rating or a word, which blocking it here doesn't change.
    /// </summary>
    public bool? TitleBlocked { get; set; }

    /// <summary>
    /// Gets or sets whether the title can't be requested by the blocklist, for everyone: the title itself, or its
    /// genre, rating or a word in its name, as a request for it would be refused. Null until it's known.
    /// </summary>
    public bool? Blocked { get; set; }

    /// <summary>
    /// Gets or sets why the title is blocked, for administrators only; null for anyone else and when it isn't.
    /// </summary>
    public string? BlockedReason { get; set; }

    /// <summary>
    /// Returns a copy with each cast and crew credit passed through <paramref name="vet"/>, such as to leave out what
    /// one viewer may not see. Details are cached and shared, so this never changes the shared one.
    /// </summary>
    /// <param name="vet">Returns the credit to show in place of each one.</param>
    /// <returns>The copy.</returns>
    public TitleDetails WithPeople(Func<PersonCredit, PersonCredit> vet)
    {
        ArgumentNullException.ThrowIfNull(vet);
        var copy = (TitleDetails)MemberwiseClone();
        copy.Cast = Cast.Select(vet).ToList();
        copy.Directors = Directors.Select(vet).ToList();
        copy.Writers = Writers.Select(vet).ToList();
        return copy;
    }

    /// <summary>
    /// Returns a copy that says whether the title is blocked. Details are cached and shared by everyone who looks
    /// the title up, so each viewer's answer is a copy rather than a change to the shared one.
    /// </summary>
    /// <param name="titleBlocked">Whether the title itself is on the blocklist.</param>
    /// <param name="reason">Why the title is blocked by any rule, or null when it isn't.</param>
    /// <param name="isAdministrator">Whether the viewer is an administrator, who is also told TitleBlocked and the reason.</param>
    /// <returns>The copy.</returns>
    public TitleDetails WithBlocked(bool titleBlocked, string? reason, bool isAdministrator)
    {
        var copy = (TitleDetails)MemberwiseClone();
        copy.Blocked = titleBlocked || reason is not null;
        copy.TitleBlocked = isAdministrator ? titleBlocked : null;
        copy.BlockedReason = isAdministrator ? reason : null;
        return copy;
    }
}
