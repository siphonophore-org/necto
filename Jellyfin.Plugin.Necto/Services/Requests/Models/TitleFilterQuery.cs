using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;


namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// A title list's filters as a request's query carries them, in Jellyfin's style: inLibrary, states and types separated
/// by commas, and genres separated by |, as the web client sends Genres.
/// </summary>
public class TitleFilterQuery
{
    /// <summary>Gets or sets whether titles must be in the library (true) or not (false); empty for either.</summary>
    public bool? InLibrary { get; set; }

    /// <summary>Gets or sets where titles must stand: NotRequested, Requested, Approved, Monitored, Downloaded or Rejected.</summary>
    public string? States { get; set; }

    /// <summary>Gets or sets the types: Movie, Series or both.</summary>
    public string? Types { get; set; }

    /// <summary>Gets or sets the genres, such as Drama|Comedy.</summary>
    public string? Genres { get; set; }

    /// <summary>Gets or sets the order a list TMDb discovers is read in: TopRated, Newest, Oldest or Title; most popular first when empty.</summary>
    public string? Sort { get; set; }

    /// <summary>Gets or sets the decades, by first year, such as 1990,2000; 1900 for anything before 1970.</summary>
    public string? Decades { get; set; }

    /// <summary>Gets or sets the least TMDb rating, 1 to 9.</summary>
    public int? MinRating { get; set; }

    /// <summary>Gets or sets the runtimes: Short (under 90 minutes), Medium (90 to 120) or Long, separated by commas.</summary>
    public string? Runtimes { get; set; }

    /// <summary>Gets or sets the original languages, as ISO 639-1 codes separated by commas.</summary>
    public string? Languages { get; set; }

    /// <summary>Gets or sets the streaming services, by TMDb's ids separated by commas.</summary>
    public string? Providers { get; set; }

    /// <summary>Gets the filter these ask for.</summary>
    /// <returns>The filter.</returns>
    public TitleFilter ToFilter() => TitleFilter.Parse(InLibrary, States, Types, Genres).WithDiscover(Sort, Decades, MinRating, Runtimes, Languages, Providers);
}
