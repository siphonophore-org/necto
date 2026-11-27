using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Xml.Serialization;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Configuration.Requests;

/// <summary>
/// The Radarr and Sonarr servers keep their settings through the settings file, keep one default per type, and resolve
/// a request to the profile it asks for or the default server's first.
/// </summary>
public class ArrServersTests
{
    /// <summary>
    /// A settings file from before the servers' newer settings loads with their defaults, and the next save marks the default.
    /// True: an upgraded server sends as it did, and Settings shows which server is the default.
    /// False: older servers would load with no search, no folders, or no default.
    /// </summary>
    [Fact]
    public void Normalize_OlderSettingsFile_LoadsWithTheDefaults()
    {
        var document = XDocument.Parse(Xml(Config(Server("a", RequestType.Movie), Server("b", RequestType.Movie), Server("s", RequestType.Series))));
        var added = new[] { "IsDefault", "SearchOnAdd", "ExternalUrl", "Tags", "MinimumAvailability", "SeriesType", "SeasonFolder" };
        document.Descendants().Where(e => added.Contains(e.Name.LocalName)).ToList().ForEach(e => e.Remove());
        Assert.DoesNotContain("SearchOnAdd", document.ToString(), StringComparison.Ordinal);

        var old = RoundTrip(document.ToString()).Requests;

        Assert.All(old.ArrServers, s =>
        {
            Assert.False(s.IsDefault);
            Assert.True(s.SearchOnAdd);
            Assert.Equal(string.Empty, s.ExternalUrl);
            var p = Assert.Single(s.Profiles);
            Assert.Empty(p.Tags);
            Assert.Equal(ArrMinimumAvailability.Released, p.MinimumAvailability);
            Assert.Equal(ArrSeriesType.Standard, p.SeriesType);
            Assert.True(p.SeasonFolder);
        });
        Assert.Equal("a", ArrServers.Resolve(old, RequestType.Movie, null)!.Value.Server.Id);
        Assert.Equal(new[] { "ap", "bp" }, ArrServers.Usable(old, RequestType.Movie).Select(u => u.Profile.Id));
        ArrServers.Normalize(old.ArrServers);
        Assert.Equal(new[] { true, false, true }, old.ArrServers.Select(s => s.IsDefault));
    }

    /// <summary>
    /// The servers' settings survive the settings file.
    /// True: a saved server reads back as it was set.
    /// False: a setting would be lost at the next restart.
    /// </summary>
    [Fact]
    public void Xml_ServerSettings_SurviveTheSettingsFile()
    {
        var server = Server("b", RequestType.Series, isDefault: true);
        server.SearchOnAdd = false;
        server.ExternalUrl = "https://sonarr.example.com";
        server.Profiles[0].Tags = new List<int> { 3, 7 };
        server.Profiles[0].SeriesType = ArrSeriesType.Anime;
        server.Profiles[0].SeasonFolder = false;
        server.Profiles[0].MinimumAvailability = ArrMinimumAvailability.InCinemas;

        var back = RoundTrip(Xml(Config(server))).Requests.ArrServers.Single();

        Assert.True(back.IsDefault);
        Assert.False(back.SearchOnAdd);
        Assert.Equal("https://sonarr.example.com", back.ExternalUrl);
        Assert.Equal(new[] { 3, 7 }, back.Profiles[0].Tags);
        Assert.Equal(ArrSeriesType.Anime, back.Profiles[0].SeriesType);
        Assert.False(back.Profiles[0].SeasonFolder);
        Assert.Equal(ArrMinimumAvailability.InCinemas, back.Profiles[0].MinimumAvailability);
    }

    /// <summary>
    /// Each type keeps exactly one default: the first marked, or the first listed.
    /// True: requests of each type go to one known server.
    /// False: two defaults would compete, or none would be chosen.
    /// </summary>
    [Fact]
    public void Normalize_Defaults_OnePerType()
    {
        var servers = new List<ArrServer>
        {
            Server("r1", RequestType.Movie),
            Server("r2", RequestType.Movie, isDefault: true),
            Server("r3", RequestType.Movie, isDefault: true),
            Server("s1", RequestType.Series),
            Server("s2", RequestType.Series)
        };

        ArrServers.Normalize(servers);

        Assert.Equal(new[] { "r2", "s1" }, servers.Where(s => s.IsDefault).Select(s => s.Id));
    }

    /// <summary>
    /// The default server comes first everywhere, a request follows its profile, and an unusable default gives way.
    /// True: new requests go to the default server, older ones to where they were sent.
    /// False: a request would go to the wrong server, or to one whose key is being re-entered.
    /// </summary>
    [Fact]
    public void Resolve_DefaultServer_ComesFirst()
    {
        var config = new RequestSettings { ArrServers = { Server("r1", RequestType.Movie), Server("r2", RequestType.Movie, isDefault: true) } };

        Assert.Equal(new[] { "r2p", "r1p" }, ArrServers.Usable(config, RequestType.Movie).Select(u => u.Profile.Id));
        Assert.Equal("r2", ArrServers.Resolve(config, RequestType.Movie, null)!.Value.Server.Id);
        Assert.Equal("r2", ArrServers.Resolve(config, RequestType.Movie, "gone")!.Value.Server.Id);
        Assert.Equal("r1", ArrServers.Resolve(config, RequestType.Movie, "r1p")!.Value.Server.Id);
        Assert.Equal("r1", ArrServers.Resolve(config, RequestType.Movie, null, defaultFirst: false)!.Value.Server.Id);
        config.ArrServers[1].ApiKey = string.Empty;
        Assert.Equal("r1", ArrServers.Resolve(config, RequestType.Movie, null)!.Value.Server.Id);
    }

    /// <summary>
    /// A request goes to the profile it asks for or the first, and only usable profiles are offered.
    /// True: a 4K request reaches the 4K Radarr, and an unfinished profile or keyless server is never used.
    /// False: a request would reach a server it cannot be added to.
    /// </summary>
    [Fact]
    public void Resolve_Profile_IsTheOneAskedForOrTheFirst()
    {
        var config = new RequestSettings
        {
            ArrServers =
            {
                new ArrServer { Id = "main", Type = RequestType.Movie, Url = "http://radarr", ApiKey = "k", Profiles = { new ArrProfileOption { Id = "hd", QualityProfileId = 1, RootFolder = "/movies" } } },
                new ArrServer { Id = "uhd", Type = RequestType.Movie, Url = "http://radarr4k", ApiKey = "k", Profiles = { new ArrProfileOption { Id = "fourk", QualityProfileId = 5, RootFolder = "/4k" }, new ArrProfileOption { Id = "unfinished" } } },
                new ArrServer { Id = "tv", Type = RequestType.Series, Url = "http://sonarr", ApiKey = string.Empty, Profiles = { new ArrProfileOption { Id = "nokey", QualityProfileId = 1, RootFolder = "/tv" } } }
            }
        };

        Assert.Equal("uhd", ArrServers.Resolve(config, RequestType.Movie, "fourk")!.Value.Server.Id);
        Assert.Equal("hd", ArrServers.Resolve(config, RequestType.Movie, "gone")!.Value.Profile.Id);
        Assert.Equal(new[] { "hd", "fourk" }, ArrServers.Usable(config, RequestType.Movie).Select(u => u.Profile.Id));
        Assert.Null(ArrServers.Resolve(config, RequestType.Series, null));
    }

    /// <summary>
    /// Saving tidies a server's external address, tags, and choices, and drops an address that is not a web one.
    /// True: Radarr and Sonarr only ever get values they accept, and the queue links only web addresses.
    /// False: a bad tag or availability would reach the server, or a script address would be linked.
    /// </summary>
    [Fact]
    public void Normalize_ServerSettings_AreTidied()
    {
        var server = Server("r1", RequestType.Movie);
        server.ExternalUrl = " https://radarr.example.com/ ";
        server.Profiles[0].Tags = new List<int> { 2, 2, 0, -1, 5 };
        server.Profiles[0].MinimumAvailability = (ArrMinimumAvailability)42;
        server.Profiles[0].SeriesType = (ArrSeriesType)42;
        var bad = Server("r2", RequestType.Movie);
        bad.ExternalUrl = "javascript:alert(1)";

        ArrServers.Normalize(new List<ArrServer> { server, bad });

        Assert.Equal("https://radarr.example.com", server.ExternalUrl);
        Assert.Equal(new[] { 2, 5 }, server.Profiles[0].Tags);
        Assert.Equal(ArrMinimumAvailability.Released, server.Profiles[0].MinimumAvailability);
        Assert.Equal(ArrSeriesType.Standard, server.Profiles[0].SeriesType);
        Assert.Equal(string.Empty, bad.ExternalUrl);
    }

    private static ArrServer Server(string id, RequestType type, bool isDefault = false)
        => new()
        {
            Id = id,
            Type = type,
            Url = "http://" + id,
            ApiKey = "k",
            IsDefault = isDefault,
            Profiles = { new ArrProfileOption { Id = id + "p", QualityProfileId = 1, RootFolder = "/media" } }
        };

    private static PluginConfiguration Config(params ArrServer[] servers)
    {
        var config = new PluginConfiguration();
        config.Requests.ArrServers.AddRange(servers);
        return config;
    }

    private static PluginConfiguration RoundTrip(string xml)
    {
        using var reader = new StringReader(xml);
        return (PluginConfiguration)new XmlSerializer(typeof(PluginConfiguration)).Deserialize(reader)!;
    }

    private static string Xml(PluginConfiguration config)
    {
        using var writer = new StringWriter();
        new XmlSerializer(typeof(PluginConfiguration)).Serialize(writer, config);
        return writer.ToString();
    }
}
