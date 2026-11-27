using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Requests;

/// <summary>
/// The Radarr and Sonarr client knows what each server looks after, adds titles with their profile's settings, and
/// keeps what it read of each library across a restart. The servers live in the loaded plugin's settings, so it runs
/// on its own.
/// </summary>
[Collection(ServicesCollection.Name)]
public sealed class ArrClientTests : IDisposable
{
    private readonly string _cache = Path.Join(Path.GetTempPath(), "necto-arr-" + Guid.NewGuid().ToString("N"));
    private RequestHarness? _harness;

    private RequestHarness Harness => _harness ??= new RequestHarness();

    public void Dispose()
    {
        _harness?.Dispose();
        if (Directory.Exists(_cache))
        {
            Directory.Delete(_cache, true);
        }
    }

    /// <summary>
    /// A movie is looked after when it is monitored or already downloaded.
    /// True: a request for a movie Radarr will fetch is refused as already waiting.
    /// False: a movie Radarr let go could not be requested, or one it watches would be asked for twice.
    /// </summary>
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void LooksAfter_Movie_MonitoredOrDownloaded(bool monitored, bool hasFile, bool expected)
        => Assert.Equal(expected, ArrClient.LooksAfter(new JsonObject { ["monitored"] = monitored, ["hasFile"] = hasFile }, RequestType.Movie, null));

    /// <summary>
    /// A show's unmonitored seasons can still be requested, and an unlisted season is not looked after.
    /// True: a viewer can ask for season 2 of a show Sonarr only fetches season 1 of.
    /// False: a season Sonarr does not want would be refused as already waiting.
    /// </summary>
    [Fact]
    public void LooksAfter_UnmonitoredSeason_CanBeRequested()
    {
        var series = Series(monitored: true, Season(1, true, 10, 10), Season(2, false, 0, 8));

        Assert.True(ArrClient.LooksAfter(series, RequestType.Series, new[] { 1 }));
        Assert.False(ArrClient.LooksAfter(series, RequestType.Series, new[] { 2 }));
        Assert.False(ArrClient.LooksAfter(series, RequestType.Series, new[] { 1, 2 }));
        Assert.False(ArrClient.LooksAfter(series, RequestType.Series, null));
        Assert.False(ArrClient.LooksAfter(series, RequestType.Series, new[] { 3 }));
    }

    /// <summary>
    /// An unmonitored season with every episode on disk is looked after.
    /// True: a finished season Sonarr stopped watching is not requested again.
    /// False: a complete season would be sent to Sonarr again.
    /// </summary>
    [Fact]
    public void LooksAfter_UnmonitoredCompleteSeason_IsLookedAfter()
    {
        var series = Series(monitored: true, Season(1, false, 8, 8), Season(2, false, 3, 8));

        Assert.True(ArrClient.LooksAfter(series, RequestType.Series, new[] { 1 }));
        Assert.False(ArrClient.LooksAfter(series, RequestType.Series, new[] { 2 }));
    }

    /// <summary>
    /// An unmonitored show looks after no season without its files.
    /// True: a show Sonarr dropped can be requested again.
    /// False: a dropped show would read as on its way.
    /// </summary>
    [Fact]
    public void LooksAfter_UnmonitoredShow_LooksAfterNoSeasonWithoutFiles()
        => Assert.False(ArrClient.LooksAfter(Series(monitored: false, Season(1, true, 0, 8)), RequestType.Series, new[] { 1 }));

    /// <summary>
    /// A show listing no seasons is taken as looked after.
    /// True: a show Sonarr has just added is not requested twice.
    /// False: a show with no season list yet could be requested again at once.
    /// </summary>
    [Fact]
    public void LooksAfter_ShowWithoutSeasons_IsLookedAfter()
        => Assert.True(ArrClient.LooksAfter(new JsonObject { ["monitored"] = true }, RequestType.Series, new[] { 1 }));

    /// <summary>
    /// A movie is added with its profile's quality, folder, availability, tags, and search choice.
    /// True: Radarr adds the movie as the profile in Settings says.
    /// False: the movie would land in the wrong folder or quality, or be searched against the setting.
    /// </summary>
    [Fact]
    public void PrepareNew_Movie_TakesItsProfilesSettings()
    {
        var item = JsonNode.Parse("""{ "title": "Dune", "tmdbId": 438631, "tags": [9] }""")!.AsObject();
        var plain = JsonNode.Parse("""{ "title": "Dune", "tmdbId": 438631 }""")!.AsObject();
        var announced = new JsonObject { ["tmdbId"] = 1 };

        ArrClient.PrepareNew(item, RequestType.Movie, 4, "/movies", new AddSettings(false, new[] { 2, 9 }, ArrMinimumAvailability.InCinemas, ArrSeriesType.Standard, true), null);
        ArrClient.PrepareNew(plain, RequestType.Movie, 4, "/movies", AddSettings.Defaults, null);
        ArrClient.PrepareNew(announced, RequestType.Movie, 1, "/m", AddSettings.Defaults with { MinimumAvailability = ArrMinimumAvailability.Announced }, null);

        Assert.Equal(4, (int)item["qualityProfileId"]!);
        Assert.Equal("/movies", (string)item["rootFolderPath"]!);
        Assert.True((bool)item["monitored"]!);
        Assert.Equal("inCinemas", (string)item["minimumAvailability"]!);
        Assert.Equal(new[] { 9, 2 }, item["tags"]!.AsArray().Select(t => (int)t!));
        Assert.False((bool)item["addOptions"]!["searchForMovie"]!);
        Assert.Null(item["seriesType"]);

        // The settings a server has before any are chosen: searched for, released, no tags.
        Assert.Equal("released", (string)plain["minimumAvailability"]!);
        Assert.True((bool)plain["addOptions"]!["searchForMovie"]!);
        Assert.Empty(plain["tags"]!.AsArray());
        Assert.Equal("announced", announced["minimumAvailability"]!.GetValue<string>());
    }

    /// <summary>
    /// A show is added with its profile's series type, season folders, tags, and only the seasons asked for monitored.
    /// True: Sonarr fetches the seasons requested, as the profile says.
    /// False: Sonarr would fetch the whole show, or file it under the wrong type.
    /// </summary>
    [Fact]
    public void PrepareNew_Show_TakesItsProfilesSettings()
    {
        var show = JsonNode.Parse("""{ "title": "Frieren", "tvdbId": 1, "seasons": [{ "seasonNumber": 0 }, { "seasonNumber": 1 }, { "seasonNumber": 2 }] }""")!.AsObject();
        var whole = JsonNode.Parse("""{ "title": "Frieren", "tvdbId": 1 }""")!.AsObject();

        ArrClient.PrepareNew(show, RequestType.Series, 6, "/tv", new AddSettings(false, new[] { 5 }, ArrMinimumAvailability.Released, ArrSeriesType.Anime, false), new List<int> { 2 });
        ArrClient.PrepareNew(whole, RequestType.Series, 6, "/tv", AddSettings.Defaults with { SeriesType = ArrSeriesType.Daily }, null);

        Assert.Equal("anime", (string)show["seriesType"]!);
        Assert.False((bool)show["seasonFolder"]!);
        Assert.Equal(new[] { 5 }, show["tags"]!.AsArray().Select(t => (int)t!));
        Assert.False((bool)show["addOptions"]!["searchForMissingEpisodes"]!);
        Assert.Null(show["addOptions"]!["monitor"]);
        Assert.Equal(new[] { false, false, true }, show["seasons"]!.AsArray().Select(s => (bool)s!["monitored"]!));
        Assert.Null(show["minimumAvailability"]);
        Assert.Equal("daily", (string)whole["seriesType"]!);
        Assert.True((bool)whole["seasonFolder"]!);
        Assert.True((bool)whole["addOptions"]!["searchForMissingEpisodes"]!);
        Assert.Equal("all", (string)whole["addOptions"]!["monitor"]!);
    }

    /// <summary>
    /// Tags are only ever added to what a title already has, each once.
    /// True: a title keeps the tags someone gave it in Radarr.
    /// False: a request would strip a title's own tags.
    /// </summary>
    [Fact]
    public void MergeTags_Existing_AreOnlyAddedTo()
    {
        Assert.Equal(new[] { 4, 1, 2 }, ArrClient.MergeTags(JsonNode.Parse("[4, 1]"), new[] { 2, 1, 2 }).Select(t => (int)t!));
        Assert.Equal(new[] { 3 }, ArrClient.MergeTags(null, new[] { 3, 0 }).Select(t => (int)t!));
        Assert.Equal(new[] { 4 }, ArrClient.MergeTags(JsonNode.Parse("[4]"), Array.Empty<int>()).Select(t => (int)t!));
    }

    /// <summary>
    /// A title the server already has is searched for what was asked: the movie, the show, or each season once.
    /// True: a request for seasons 1 and 3 searches those seasons.
    /// False: the whole show would be searched, or a season twice.
    /// </summary>
    [Fact]
    public void SearchCommands_ExistingTitle_SearchesWhatWasAsked()
    {
        Assert.Equal("""{"name":"MoviesSearch","movieIds":[7]}""", Assert.Single(ArrClient.SearchCommands(RequestType.Movie, 7, null)).ToJsonString());
        Assert.Equal("""{"name":"SeriesSearch","seriesId":9}""", Assert.Single(ArrClient.SearchCommands(RequestType.Series, 9, null)).ToJsonString());
        Assert.Equal(
            new[] { """{"name":"SeasonSearch","seriesId":9,"seasonNumber":1}""", """{"name":"SeasonSearch","seriesId":9,"seasonNumber":3}""" },
            ArrClient.SearchCommands(RequestType.Series, 9, new List<int> { 3, 1, 3 }).Select(c => c.ToJsonString()));
    }

    /// <summary>
    /// A server's tags are read by label, without bad entries, and a profile's tags reach the add settings once each.
    /// True: Settings lists the server's tags by name, and a title gets the tags chosen.
    /// False: a broken answer would fail the page, or a tag would be sent twice.
    /// </summary>
    [Fact]
    public void MapTags_ServersAnswer_ReadsByLabel()
    {
        var tags = ArrClient.MapTags("""[{ "id": 2, "label": "requests" }, { "id": 1, "label": "4k" }, { "id": 0, "label": "bad" }, { "id": 3 }, { "id": 2, "label": "again" }]""");
        var server = new ArrServer { Id = "r1", Type = RequestType.Movie, Url = "http://r1", ApiKey = "k", SearchOnAdd = false, Profiles = { new ArrProfileOption { Id = "p", QualityProfileId = 1, RootFolder = "/media", Tags = new List<int> { 1, 1, 0 } } } };

        var add = AddSettings.From(server, server.Profiles[0]);

        Assert.Equal(new[] { (1, "4k"), (2, "requests") }, tags.Select(t => (t.Id, t.Label)));
        Assert.Empty(ArrClient.MapTags("not json"));
        Assert.Empty(ArrClient.MapTags("""{ "message": "Unauthorized" }"""));
        Assert.False(add.Search);
        Assert.Equal(new[] { 1 }, add.Tags);
    }

    /// <summary>
    /// Sonarr's special is found by the day it aired, then by its number.
    /// True: TMDb's third special finds Sonarr's episode of the same day, under another number.
    /// False: the wrong special would be fetched, or none.
    /// </summary>
    [Fact]
    public void MatchSpecials_ByDayThenNumber_FindsSonarrsEpisode()
    {
        var episodes = new List<JsonObject>
        {
            new() { ["id"] = 11, ["episodeNumber"] = 1, ["airDate"] = "2019-12-25" },
            new() { ["id"] = 12, ["episodeNumber"] = 2, ["airDate"] = "2020-12-25" },
            new() { ["id"] = 13, ["episodeNumber"] = 3, ["airDate"] = "2021-06-01" },
            new() { ["id"] = 14, ["episodeNumber"] = 4, ["airDate"] = "2021-06-01" }
        };

        var found = ArrClient.MatchSpecials(episodes, new[] { Special(3, "2020-12-25"), Special(4, "2021-06-01"), Special(1), Special(40, "2030-01-01") });

        Assert.Equal(new[] { 12, 14, 11 }, found.Select(e => e["id"]!.GetValue<int>()));
    }

    /// <summary>
    /// The servers are read again whenever the settings change, by a mutation or a whole save.
    /// True: a new Radarr address is used at once.
    /// False: requests would keep going to a server that was replaced or removed.
    /// </summary>
    [Fact]
    public void Servers_ChangedSettings_AreReadAgain()
    {
        Harness.UseRadarr();

        Assert.Equal(new[] { ("Radarr", RequestHarness.RadarrUrl, "key") }, ArrClient.Servers(RequestType.Movie));
        Assert.True(ArrClient.IsConfigured(RequestType.Movie));
        Assert.False(ArrClient.IsConfigured(RequestType.Series));

        Harness.Plugin.MutateConfiguration(c => { c.Requests.ArrServers = new List<ArrServer> { Radarr("4K Radarr", "http://radarr4k.test", "other") }; return false; });
        Assert.Equal(new[] { ("4K Radarr", "http://radarr4k.test", "other") }, ArrClient.Servers(RequestType.Movie));

        // The whole configuration replaced, as the settings page saves it.
        var saved = Harness.Plugin.ReadConfiguration(c => c.Requests.ArrServers[0].ApiKey);
        var replaced = new Jellyfin.Plugin.Necto.Configuration.PluginConfiguration();
        replaced.Requests.ArrServers.Add(Radarr("Radarr HD", "http://radarrhd.test", saved));
        Harness.Plugin.UpdateConfiguration(replaced);
        Assert.Equal(("Radarr HD", "http://radarrhd.test", "other"), Assert.Single(ArrClient.Servers(RequestType.Movie)));

        Harness.Plugin.MutateConfiguration(c => { c.Requests.ArrServers = new List<ArrServer>(); return false; });
        Assert.Empty(ArrClient.Servers(RequestType.Movie));
        Assert.False(ArrClient.IsConfigured(RequestType.Movie));
    }

    /// <summary>
    /// A library read is kept in the cache folder and still there after a restart, with nothing asked again.
    /// True: the first visit after a restart marks titles without asking Radarr.
    /// False: every restart would ask Radarr for its whole library, or about each title.
    /// </summary>
    [Fact]
    public async Task LoadLibraries_AfterARestart_KeepsTheRead()
    {
        Harness.UseRadarr();
        using var http = new OutsideServers();
        using (var before = Arr(http))
        {
            Assert.False(before.LibrariesAreCurrent);
            await before.RefreshLibrariesAsync(CancellationToken.None);
            Assert.True(before.LibrariesAreCurrent);
        }

        using var after = Arr(http);
        Assert.False(after.LibrariesAreCurrent);
        after.LoadLibraries();
        var held = await after.HeldAsync(RequestType.Movie, new[] { "603", "604", "605" }, CancellationToken.None);

        Assert.True(after.LibrariesAreCurrent);
        Assert.Equal(new[] { "603", "604" }, held.Ids.Order());
        Assert.Equal(new[] { "603" }, held.WithFiles);
        Assert.Equal(new[] { "radarr.test/api/v3/movie" }, http.Asked);
    }

    /// <summary>
    /// A library read hours ago still marks the lists.
    /// True: a restart in the afternoon uses the morning's read.
    /// False: a read a few hours old would be thrown away and each title asked about.
    /// </summary>
    [Fact]
    public async Task HeldAsync_ReadHoursAgo_StillMarks()
    {
        Harness.UseRadarr();
        KeepLibrary(DateTime.UtcNow.AddHours(-6));
        using var http = new OutsideServers();
        using var arr = Arr(http);
        arr.LoadLibraries();

        var held = await arr.HeldAsync(RequestType.Movie, new[] { "603", "604", "605" }, CancellationToken.None);

        Assert.Equal(new[] { "603", "604" }, held.Ids.Order());
        Assert.Empty(http.Asked);
        Assert.True(arr.LibrariesAreCurrent);
    }

    /// <summary>
    /// What a server said of one title is kept until the refresh lets it go.
    /// True: a title asked about twice is asked once, and again after the refresh.
    /// False: every view of a title would ask Radarr, or an answer would never be read again.
    /// </summary>
    [Fact]
    public async Task ClearHeld_AtTheRefresh_LetsAnswersGo()
    {
        Harness.UseRadarr();
        using var http = new OutsideServers();
        using var arr = Arr(http);

        await arr.HeldAsync(RequestType.Movie, new[] { "605" }, CancellationToken.None);
        await arr.HeldAsync(RequestType.Movie, new[] { "605" }, CancellationToken.None);
        Assert.Single(http.Asked);

        arr.ClearHeld();
        await arr.HeldAsync(RequestType.Movie, new[] { "605" }, CancellationToken.None);
        Assert.Equal(2, http.Asked.Count);
    }

    /// <summary>
    /// A library read days ago is not current, so a title is asked about again.
    /// True: a server's state days old is never trusted.
    /// False: a title Radarr fetched since would still show as missing.
    /// </summary>
    [Fact]
    public async Task HeldAsync_ReadDaysAgo_AsksAgain()
    {
        Harness.UseRadarr();
        KeepLibrary(DateTime.UtcNow.AddDays(-3));
        using var http = new OutsideServers();
        using var arr = Arr(http);
        arr.LoadLibraries();

        Assert.False(arr.LibrariesAreCurrent);
        await arr.HeldAsync(RequestType.Movie, new[] { "603" }, CancellationToken.None);
        Assert.Equal(new[] { "radarr.test/api/v3/movie?tmdbId=603" }, http.Asked);
    }

    private ArrClient Arr(OutsideServers http)
        => new(NullLogger<ArrClient>.Instance, InterfaceStub.Of<IApplicationPaths>(("get_CachePath", _ => _cache)), http);

    private void KeepLibrary(DateTime readUtc)
        => File.WriteAllText(
            Path.Join(Directory.CreateDirectory(Path.Join(_cache, "requests")).FullName, "arr-libraries.json"),
            JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["Movie|radarr1"] = new { AtUtc = readUtc, Files = new Dictionary<string, bool> { ["603"] = true, ["604"] = false }, ByTmdb = true }
            }));

    private static ArrServer Radarr(string name, string url, string key) => new()
    {
        Id = "radarr1",
        Name = name,
        Type = RequestType.Movie,
        Url = url,
        ApiKey = key,
        IsDefault = true,
        Profiles = { new ArrProfileOption { Id = "hd", Name = "HD", QualityProfileId = 4, RootFolder = "/movies" } }
    };

    private static SpecialEpisode Special(int number, string? aired = null)
        => new() { Number = number, AirDate = aired is null ? null : DateTime.Parse(aired, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal) };

    private static JsonObject Season(int number, bool monitored, int files, int total)
        => new()
        {
            ["seasonNumber"] = number,
            ["monitored"] = monitored,
            ["statistics"] = new JsonObject { ["episodeFileCount"] = files, ["totalEpisodeCount"] = total, ["episodeCount"] = files }
        };

    private static JsonObject Series(bool monitored, params JsonObject[] seasons)
    {
        var list = new JsonArray();
        foreach (var season in seasons)
        {
            list.Add(season);
        }

        return new JsonObject { ["monitored"] = monitored, ["seasons"] = list };
    }
}
