using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Siphonophore.Stolon;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Requests;

/// <summary>
/// A save of the Requests and Issues settings keeps keys by the kept sentinel only at their address, cleans ids, lists,
/// and home rows, holds numbers in range, and keeps what only Necto changes as it is stored.
/// </summary>
[Collection(ServicesCollection.Name)]
public class RequestSettingsNormalizerTests
{
    private static readonly SecretProtector Secrets = new("Jellyfin.Plugin.Necto.Tests", NullLogger.Instance);

    /// <summary>
    /// A kept key stays only while its server's address is unchanged, a typed key replaces it, and an encrypted key
    /// copied from elsewhere is dropped.
    /// True: a key never follows a server to a new address, and the page never needs the key itself.
    /// False: pointing a server elsewhere would send the stored key there.
    /// </summary>
    [Fact]
    public void Requests_Keys_AreKeptOnlyAtTheirAddress()
    {
        var stored = new RequestSettings { TmdbApiKey = "enc:v1:tmdb", ArrServers = { Radarr("r1", "http://radarr:7878/", "enc:v1:key"), Radarr("r2", "http://other:7878", "enc:v1:other") } };
        var incoming = new RequestSettings
        {
            TmdbApiKey = SecretProtector.KeptSentinel,
            ArrServers =
            {
                Radarr("r1", "http://RADARR:7878", SecretProtector.KeptSentinel),
                Radarr("r2", "http://evil.example.com", SecretProtector.KeptSentinel),
                Radarr("r3", "http://new:7878", "typed"),
                Radarr("r4", "http://copy:7878", "enc:v1:key")
            }
        };

        var saved = RequestSettingsNormalizer.Requests(incoming, stored, Secrets);

        Assert.Equal("enc:v1:tmdb", saved.TmdbApiKey);
        Assert.Equal(new[] { "enc:v1:key", string.Empty, "typed", string.Empty }, saved.ArrServers.Select(s => s.ApiKey));
    }

    /// <summary>
    /// Servers of no known type are dropped, ids are cleaned and kept once, and a profile id used twice gets a new one.
    /// True: every server and profile has one id of letters and digits a request can name.
    /// False: a crafted id would be stored, or two profiles would share one id.
    /// </summary>
    [Fact]
    public void Requests_Ids_AreCleanedAndUnique()
    {
        var incoming = new RequestSettings
        {
            ArrServers =
            {
                Radarr("r1", "http://a", "k"),
                Radarr("r1", "http://b", "k"),
                Radarr("bad id!", "http://c", "k"),
                new ArrServer { Id = "x", Type = (RequestType)42, Url = "http://d" }
            }
        };
        incoming.ArrServers[0].Profiles[0].Id = "hd";
        incoming.ArrServers[2].Profiles[0].Id = "hd";

        var saved = RequestSettingsNormalizer.Requests(incoming, new RequestSettings(), Secrets);

        Assert.Equal(2, saved.ArrServers.Count);
        Assert.Equal("r1", saved.ArrServers[0].Id);
        Assert.Equal("http://a", saved.ArrServers[0].Url);
        Assert.Matches("^[0-9a-f]{32}$", saved.ArrServers[1].Id);
        Assert.Equal("hd", saved.ArrServers[0].Profiles[0].Id);
        Assert.NotEqual("hd", saved.ArrServers[1].Profiles[0].Id);
        Assert.Single(saved.ArrServers, s => s.IsDefault);
    }

    /// <summary>
    /// Numbers are held in range, an unknown refresh choice is the item folder, and lists are trimmed and kept once.
    /// True: a page posting bad values saves settings the plugin can use.
    /// False: a negative window or an unknown choice would be stored.
    /// </summary>
    [Fact]
    public void Requests_NumbersAndLists_AreHeldInRange()
    {
        var incoming = new RequestSettings
        {
            RetentionDays = -5,
            MovieQuotaDays = 0,
            SeriesQuotaDays = 9999,
            AutoDeclineAfterDays = 99999,
            AgingAfterDays = -1,
            RefreshOnAvailable = (LibraryRefresh)42,
            AutoDeclineReason = "  " + new string('x', 600),
            BlockedGenres = { " Horror ", "horror", "", "Reality" },
            BlockedLanguages = { "JA", "english", "ko", "ja", "x1" },
            BlockedTags = { new TmdbName { Id = 1, Name = "a" }, new TmdbName { Id = 1, Name = "b" }, new TmdbName { Id = 0, Name = "none" } }
        };

        var saved = RequestSettingsNormalizer.Requests(incoming, new RequestSettings(), Secrets);

        Assert.Equal(0, saved.RetentionDays);
        Assert.Equal(1, saved.MovieQuotaDays);
        Assert.Equal(365, saved.SeriesQuotaDays);
        Assert.Equal(3650, saved.AutoDeclineAfterDays);
        Assert.Equal(0, saved.AgingAfterDays);
        Assert.Equal(LibraryRefresh.ItemFolder, saved.RefreshOnAvailable);
        Assert.Equal(500, saved.AutoDeclineReason.Length);
        Assert.Equal(new[] { "Horror", "Reality" }, saved.BlockedGenres);
        Assert.Equal(new[] { "ja", "ko" }, saved.BlockedLanguages);
        Assert.Equal("a", Assert.Single(saved.BlockedTags).Name);
    }

    /// <summary>
    /// Home rows need a heading, a known kind, and an id, keep only digit ids, get an id of their own, and the order and
    /// hidden rows keep only keys a row has.
    /// True: the home page shows only rows it can fill, in the order saved.
    /// False: a broken row would fail the home page, or the order would name rows that are gone.
    /// </summary>
    [Fact]
    public void Requests_HomeRows_AreCleaned()
    {
        var incoming = new RequestSettings
        {
            HomeRows =
            {
                new HomeRow { Id = "a", Title = "Drama", Type = RequestType.Movie, Kind = "Genre", Ids = "18, x, 18,35", Sort = "Sideways" },
                new HomeRow { Id = "b", Title = " ", Type = RequestType.Movie, Kind = "Genre", Ids = "18" },
                new HomeRow { Id = "c", Title = "HBO", Type = RequestType.Movie, Kind = "Network", Ids = "49" },
                new HomeRow { Id = "d", Title = "None", Type = RequestType.Movie, Kind = "Keyword", Ids = "abc" },
                new HomeRow { Id = "bad id!", Title = "Anime", Type = RequestType.Series, Kind = "Keyword", Ids = "210024" }
            },
            HomeRowOrder = { "Custom-a", "Custom-b", "TrendingShows", "TrendingShows" },
            HiddenHomeRows = { "UpcomingShows", "Custom-a", "Gone" }
        };

        var saved = RequestSettingsNormalizer.Requests(incoming, new RequestSettings(), Secrets);

        Assert.Equal(2, saved.HomeRows.Count);
        Assert.Equal("18,35", saved.HomeRows[0].Ids);
        Assert.Equal(string.Empty, saved.HomeRows[0].Sort);
        Assert.Matches("^[0-9a-f]{12}$", saved.HomeRows[1].Id);
        Assert.Equal(new[] { "Custom-a", "TrendingShows" }, saved.HomeRowOrder);
        Assert.Equal(new[] { "UpcomingShows" }, saved.HiddenHomeRows);
    }

    /// <summary>
    /// Blocked titles and the menu link flag keep what is stored, whatever the page posts.
    /// True: a block made since the page loaded survives its save, and Necto alone decides the menu link.
    /// False: an older page would undo a block, or a save would add the menu link again.
    /// </summary>
    [Fact]
    public void Requests_BlockedTitlesAndMenuLink_KeepWhatIsStored()
    {
        var stored = new RequestSettings
        {
            MenuLinkAdded = true,
            BlockedTitles = { new BlockedTitle { Type = RequestType.Movie, Title = "Cats", Tmdb = "536869" }, new BlockedTitle { Type = RequestType.Movie, Title = "No ids" } }
        };
        var incoming = new RequestSettings { MenuLinkAdded = false, BlockedTitles = { new BlockedTitle { Type = RequestType.Movie, Title = "Posted", Tmdb = "1" } } };

        var saved = RequestSettingsNormalizer.Requests(incoming, stored, Secrets);

        Assert.Equal("Cats", Assert.Single(saved.BlockedTitles).Title);
        Assert.True(saved.MenuLinkAdded);
    }

    /// <summary>
    /// The Issues settings are held in range, and the menu link flag keeps what is stored.
    /// True: a page cannot save a negative retention or turn the menu link flag on or off.
    /// False: issues would be cleaned at once, or the Issues link would be added again.
    /// </summary>
    [Fact]
    public void Issues_Settings_AreHeldInRange()
    {
        var saved = RequestSettingsNormalizer.Issues(new IssueSettings { Enabled = true, RetentionDays = -1, LimitDays = 0, MenuLinkAdded = true }, new IssueSettings { MenuLinkAdded = false });
        var long_ = RequestSettingsNormalizer.Issues(new IssueSettings { RetentionDays = 99999, LimitDays = 9999 }, new IssueSettings { MenuLinkAdded = true });

        Assert.True(saved.Enabled);
        Assert.Equal(0, saved.RetentionDays);
        Assert.Equal(1, saved.LimitDays);
        Assert.False(saved.MenuLinkAdded);
        Assert.Equal(3650, long_.RetentionDays);
        Assert.Equal(365, long_.LimitDays);
        Assert.True(long_.MenuLinkAdded);
    }

    /// <summary>
    /// A whole configuration's Requests and Issues sections are cleaned, and missing ones are made.
    /// True: a save from any page leaves both sections usable.
    /// False: a save without a section would store null.
    /// </summary>
    [Fact]
    public void Normalize_Configuration_CleansBothSections()
    {
        var incoming = new PluginConfiguration { Requests = null!, Issues = new IssueSettings { LimitDays = 0 } };

        RequestSettingsNormalizer.Normalize(incoming, new PluginConfiguration(), Secrets);

        Assert.NotNull(incoming.Requests);
        Assert.Equal(1, incoming.Issues.LimitDays);
    }

    /// <summary>
    /// A save through the plugin runs the cleaning, keeping a stored key by its sentinel.
    /// True: the dashboard's save keeps Radarr's key without ever showing it.
    /// False: a save from the settings page would clear the stored key.
    /// </summary>
    [Fact]
    public void UpdateConfiguration_Save_RunsTheCleaning()
    {
        var plugin = TestPlugin.CreateWithSettings();
        plugin.MutateConfiguration(c => { c.Requests.ArrServers.Add(Radarr("r1", "http://radarr", "enc:v1:key")); return false; });
        var incoming = new PluginConfiguration();
        incoming.Requests.ArrServers.Add(Radarr("r1", "http://radarr", SecretProtector.KeptSentinel));
        incoming.Requests.MovieQuotaDays = 0;

        plugin.UpdateConfiguration(incoming);

        Assert.Equal("enc:v1:key", plugin.Configuration.Requests.ArrServers[0].ApiKey);
        Assert.Equal(1, plugin.Configuration.Requests.MovieQuotaDays);
    }

    private static ArrServer Radarr(string id, string url, string key) => new()
    {
        Id = id,
        Name = "Radarr",
        Type = RequestType.Movie,
        Url = url,
        ApiKey = key,
        Profiles = { new ArrProfileOption { Id = id + "p", QualityProfileId = 1, RootFolder = "/movies" } }
    };
}
