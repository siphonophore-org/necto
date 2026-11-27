using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Services.Issues.Models;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Siphonophore.Stolon;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Requests;

/// <summary>
/// The request database keeps every request, held message, browser, and issue in a real SQLite file, pages and counts
/// them as the pages ask, and brings any older file up to the current schema.
/// </summary>
public sealed class RequestDatabaseTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "necto-requests-db-" + Guid.NewGuid().ToString("N"));
    private readonly RequestDatabase _db;

    public RequestDatabaseTests()
    {
        SQLitePCL.Batteries_V2.Init();
        _db = new RequestDatabase(Path.Combine(_dir, "requests.db"), NullLogger<RequestDatabase>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    private static MediaRequest Make(string title, RequestStatus status = RequestStatus.Pending, Guid? user = null, DateTime? created = null, RequestType type = RequestType.Movie) => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        Title = title,
        ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Tmdb"] = title.GetHashCode().ToString(System.Globalization.CultureInfo.InvariantCulture) },
        UserId = user ?? Guid.NewGuid(),
        UserName = "viewer",
        CreatedUtc = created ?? DateTime.UtcNow,
        UpdatedUtc = created ?? DateTime.UtcNow,
        Status = status
    };

    /// <summary>
    /// A page is filtered by any genre named, case blind and literally, and requests without genres are listed apart.
    /// True: the queue's genre filter shows the requests of those genres.
    /// False: a genre filter would miss requests, match a wildcard, or never fill in missing genres.
    /// </summary>
    [Fact]
    public void Page_Genres_FilterByAnyGenreNamed()
    {
        var drama = Make("Drama One");
        drama.Genres = new List<string> { "Drama", "Ciencia ficción" };
        var melodrama = Make("Melodrama One");
        melodrama.Genres = new List<string> { "Melodrama" };
        var unknown = Make("Unknown Genres");
        _db.Insert(drama);
        _db.Insert(melodrama);
        _db.Insert(unknown);

        Assert.Equal(new[] { "Drama One" }, _db.Page(new PagedQuery(), genres: new[] { "drama" }).Items.Select(r => r.Title));
        Assert.Equal(new[] { "Drama One" }, _db.Page(new PagedQuery(), genres: new[] { "Ciencia ficción" }).Items.Select(r => r.Title));
        Assert.Equal(2, _db.Page(new PagedQuery(), genres: new[] { "Drama", "Melodrama" }).TotalCount);
        Assert.Empty(_db.Page(new PagedQuery(), genres: new[] { "100%_Drama" }).Items);
        Assert.Equal(3, _db.Page(new PagedQuery()).TotalCount);
        Assert.Equal(new[] { "Unknown Genres" }, _db.WithoutGenres(10).Select(r => r.Title));
    }

    /// <summary>
    /// The viewer's page adds everyone's scheduled requests of the types they may request, under the same filters.
    /// True: a viewer sees what is coming beside their own and joined requests.
    /// False: others' waiting or rejected requests would show, or the filters would not hold.
    /// </summary>
    [Fact]
    public void Page_WithScheduled_AddsEveryonesScheduledRequests()
    {
        var me = Guid.NewGuid();
        var someone = Guid.NewGuid();
        var mine = Make("Mine Waiting", RequestStatus.Pending, me);
        var joined = Make("Joined Waiting", RequestStatus.Pending, someone);
        joined.Joined = new List<RequestJoiner> { new() { UserId = me, UserName = "Me", JoinedUtc = DateTime.UtcNow } };
        var approved = Make("Their Approved", RequestStatus.Approved, someone);
        var failed = Make("Their Failed", RequestStatus.Failed, someone);
        var partShow = Make("Their Partial Show", RequestStatus.PartiallyAvailable, someone, type: RequestType.Series);
        var waiting = Make("Their Waiting", RequestStatus.Pending, someone);
        var rejected = Make("Their Rejected", RequestStatus.Declined, someone);
        var dropped = Make("Their Dropped", RequestStatus.Approved, someone);
        dropped.RemovedFromArr = true;
        foreach (var r in new[] { mine, joined, approved, failed, partShow, waiting, rejected, dropped })
        {
            _db.Insert(r);
        }

        string[] Titles(PagedResult<MediaRequest> page) => page.Items.Select(r => r.Title).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(
            new[] { "Joined Waiting", "Mine Waiting", "Their Approved", "Their Failed", "Their Partial Show" },
            Titles(_db.Page(new PagedQuery(), me, withJoined: true, withScheduled: (true, null))));
        // Others' scheduled ones only of the type the viewer may request, and the other filters hold across all of them.
        Assert.Equal(new[] { "Joined Waiting", "Mine Waiting", "Their Approved", "Their Failed" }, Titles(_db.Page(new PagedQuery(), me, withJoined: true, withScheduled: (true, RequestType.Movie))));
        Assert.Equal(new[] { "Their Partial Show" }, Titles(_db.Page(new PagedQuery(), me, withJoined: true, type: RequestType.Series, withScheduled: (true, null))));
        Assert.Equal(new[] { "Joined Waiting", "Mine Waiting" }, Titles(_db.Page(new PagedQuery { Filter = "Pending" }, me, withJoined: true, withScheduled: (true, null))));
    }

    /// <summary>
    /// The open requests are one kept list, read again after every write.
    /// True: search marks titles from the current open requests without reading them each time.
    /// False: a new or closed request would be missed by search.
    /// </summary>
    [Fact]
    public void OpenRequests_AfterAWrite_AreReadAgain()
    {
        var waiting = Make("Waiting");
        _db.Insert(waiting);
        Assert.Same(_db.OpenRequests(), _db.OpenRequests());
        Assert.Equal(new[] { "Waiting" }, _db.OpenRequests().Select(r => r.Title));

        waiting.Status = RequestStatus.Declined;
        _db.Update(waiting);
        Assert.Empty(_db.OpenRequests());

        _db.Insert(Make("Another"));
        Assert.Equal(new[] { "Another" }, _db.OpenRequests().Select(r => r.Title));
    }

    /// <summary>
    /// Only waiting, sent, and available requests still in Radarr count toward the open limit.
    /// True: rejected and dropped requests free their place.
    /// False: a requester would be held back by requests that no longer count.
    /// </summary>
    [Fact]
    public void CountHoldingAPlace_Requests_CountsOnlyThoseHoldingOne()
    {
        var me = Guid.NewGuid();
        var dropped = Make("Dropped", RequestStatus.Approved, me);
        dropped.RemovedFromArr = true;
        foreach (var r in new[] { Make("Waiting", RequestStatus.Pending, me), Make("Sent", RequestStatus.Approved, me), Make("Done", RequestStatus.Available, me), Make("Rejected", RequestStatus.Declined, me), dropped, Make("Someone Else's") })
        {
            _db.Insert(r);
        }

        Assert.Equal(_db.List(me).Count(r => r.HoldsAPlace), _db.CountHoldingAPlace(me));
        Assert.Equal(2, _db.CountHoldingAPlace(me));
    }

    /// <summary>
    /// Every field of a request is stored and read back as it was.
    /// True: nothing a request holds is lost between restarts.
    /// False: a field added to requests would be dropped by the database.
    /// </summary>
    [Fact]
    public void Insert_EveryField_RoundTrips()
    {
        var r = Make("Inception", RequestStatus.PartiallyAvailable, type: RequestType.Series);
        r.Year = 2010;
        r.Seasons = new List<int> { 1, 3 };
        r.AvailableSeasons = new List<int> { 1 };
        r.RequesterNote = "4K please";
        r.Note = "note";
        r.DecidedBy = "admin";
        r.LibraryItemId = Guid.NewGuid();
        r.ArrId = 42;
        r.Progress = 0.5;
        r.ProgressText = "Downloading 50%";
        r.SendFailures = 2;
        r.RetryAfterUtc = new DateTime(2030, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        r.ServerId = "sonarr1";
        r.ProfileId = "tv";
        r.QuotaCost = 2;
        r.Overview = "A thief who steals secrets.";
        r.PosterUrl = "https://image.tmdb.org/t/p/w342/p.jpg";
        r.BackdropUrl = "https://image.tmdb.org/t/p/w1280/b.jpg";
        r.ExpectedEpisodes = new Dictionary<int, int> { [1] = 10, [3] = 8 };
        r.RemovedFromArr = true;
        r.MarkedAvailable = true;
        r.DownloadDoneUtc = new DateTime(2030, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        r.AgingNotifiedUtc = new DateTime(2030, 1, 3, 4, 5, 6, DateTimeKind.Utc);
        r.ExpiringNotifiedDays = 2;
        r.DecidedUtc = new DateTime(2030, 1, 4, 5, 6, 7, DateTimeKind.Utc);
        r.Genres = new List<string> { "Science Fiction", "Ciencia ficción" };
        r.SeasonProgress = new List<SeasonProgress> { new() { Season = 1, Files = 4, Episodes = 10, Downloading = 2 } };
        r.Specials = new List<SpecialEpisode> { new() { Number = 3, Name = "Christmas Special", AirDate = new DateTime(2020, 12, 25, 0, 0, 0, DateTimeKind.Utc) } };
        r.Joined = new List<RequestJoiner> { new() { UserId = Guid.NewGuid(), UserName = "Dana", JoinedUtc = new DateTime(2030, 1, 5, 6, 7, 8, DateTimeKind.Utc) } };

        // Every stored property holds a value unlike its default, so one the database drops shows up below.
        var fresh = new MediaRequest();
        foreach (var property in typeof(MediaRequest).GetProperties().Where(p => p.CanWrite))
        {
            Assert.False(
                System.Text.Json.JsonSerializer.Serialize(property.GetValue(r)) == System.Text.Json.JsonSerializer.Serialize(property.GetValue(fresh)),
                property.Name + " is left at its default, so the round trip does not test it");
        }

        _db.Insert(r);

        var back = _db.Get(r.Id)!;
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(r), System.Text.Json.JsonSerializer.Serialize(back));
        Assert.Equal(r.Title, back.Title);
        Assert.Equal(r.Type, back.Type);
        Assert.Equal(r.Year, back.Year);
        Assert.Equal(r.Seasons, back.Seasons);
        Assert.Equal(r.AvailableSeasons, back.AvailableSeasons);
        Assert.Equal(r.RequesterNote, back.RequesterNote);
        Assert.Equal(r.LibraryItemId, back.LibraryItemId);
        Assert.Equal(42, back.ArrId);
        Assert.Equal(0.5, back.Progress);
        Assert.Equal(2, back.SendFailures);
        Assert.Equal(r.RetryAfterUtc, back.RetryAfterUtc);
        Assert.Equal(r.CreatedUtc, back.CreatedUtc);
        Assert.Equal(r.ProviderIds["tmdb"], back.ProviderIds["TMDB"]);
        Assert.Equal("sonarr1", back.ServerId);
        Assert.Equal("tv", back.ProfileId);
        Assert.Equal(2, back.QuotaCost);
    }

    /// <summary>
    /// An update is stored, and a delete removes the request once.
    /// True: a decision sticks, and a removed request is gone.
    /// False: a decision would be lost, or a delete would report a request it did not find.
    /// </summary>
    [Fact]
    public void Update_AndDelete_ChangeTheStoredRequest()
    {
        var r = Make("Heat");
        _db.Insert(r);
        r.Status = RequestStatus.Declined;
        r.Note = "No";
        _db.Update(r);
        Assert.Equal(RequestStatus.Declined, _db.Get(r.Id)!.Status);
        Assert.True(_db.Delete(r.Id));
        Assert.Null(_db.Get(r.Id));
        Assert.False(_db.Delete(r.Id));
    }

    /// <summary>
    /// Pages come newest first, filtered by one or more statuses and searched by title, with wildcards taken literally.
    /// True: the queue pages, filters, and searches as its controls say.
    /// False: a page would be out of order, ignore a filter, or treat % as a wildcard.
    /// </summary>
    [Fact]
    public void Page_FilterAndSearch_NewestFirst()
    {
        var start = DateTime.UtcNow.AddDays(-10);
        for (var i = 0; i < 25; i++)
        {
            _db.Insert(Make("Movie " + i, i % 5 == 0 ? RequestStatus.Declined : RequestStatus.Pending, created: start.AddHours(i)));
        }

        var first = _db.Page(new PagedQuery { Take = 10 });
        Assert.Equal(25, first.TotalCount);
        Assert.Equal("Movie 24", first.Items[0].Title);
        Assert.True(first.HasMore);

        var declined = _db.Page(new PagedQuery { Filter = "Declined", Take = 50 });
        Assert.Equal(5, declined.TotalCount);

        var several = _db.Page(new PagedQuery { Filter = "Pending, Declined", Take = 50 });
        Assert.Equal(25, several.TotalCount);
        Assert.Equal(5, _db.Page(new PagedQuery { Filter = "Declined,Nonsense", Take = 50 }).TotalCount);

        var open = _db.Page(new PagedQuery { Filter = "Open", Take = 50 });
        Assert.Equal(20, open.TotalCount);

        var search = _db.Page(new PagedQuery { Search = "movie 1", Take = 50 });
        Assert.Equal(11, search.TotalCount);

        // Wildcards in a search are matched literally.
        Assert.Equal(0, _db.Page(new PagedQuery { Search = "%", Take = 50 }).TotalCount);
    }

    /// <summary>
    /// The quota counts a user's requests of one type in the window, without rejected ones.
    /// True: a rejected request does not use up the quota.
    /// False: a requester's quota would count rejections or other types.
    /// </summary>
    [Fact]
    public void RequestsSince_Quota_LeavesOutDeclines()
    {
        var user = Guid.NewGuid();
        _db.Insert(Make("A", user: user, created: DateTime.UtcNow.AddDays(-2)));
        _db.Insert(Make("B", RequestStatus.Declined, user, DateTime.UtcNow.AddDays(-1)));
        _db.Insert(Make("C", RequestStatus.Available, user, DateTime.UtcNow.AddDays(-20)));
        _db.Insert(Make("D", user: user, type: RequestType.Series));

        var counted = _db.RequestsSince(user, RequestType.Movie, DateTime.UtcNow.AddDays(-7));
        Assert.Equal("A", Assert.Single(counted).Title);
    }

    /// <summary>
    /// Cleanup removes only finished requests older than the retention.
    /// True: old declined and available requests go, waiting ones stay.
    /// False: cleanup would remove a request still waiting.
    /// </summary>
    [Fact]
    public void DeleteClosedBefore_OldFinished_AreRemoved()
    {
        var old = DateTime.UtcNow.AddDays(-100);
        _db.Insert(Make("Old declined", RequestStatus.Declined, created: old));
        _db.Insert(Make("Old available", RequestStatus.Available, created: old));
        _db.Insert(Make("Old pending", RequestStatus.Pending, created: old));
        _db.Insert(Make("New declined", RequestStatus.Declined));

        Assert.Equal(2, _db.DeleteClosedBefore(DateTime.UtcNow.AddDays(-90)).Count);
        Assert.Equal(new[] { "New declined", "Old pending" }, _db.List().Select(r => r.Title).Order().ToArray());
    }

    /// <summary>
    /// Which reminders were sent is kept.
    /// True: a reminder is sent once after a restart.
    /// False: reminders would repeat after a restart.
    /// </summary>
    [Fact]
    public void Update_Reminders_AreKept()
    {
        var r = Make("Dune");
        _db.Insert(r);
        r.AgingNotifiedUtc = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
        r.ExpiringNotifiedDays = 2;
        _db.Update(r);
        var back = _db.Get(r.Id)!;
        Assert.Equal(r.AgingNotifiedUtc, back.AgingNotifiedUtc);
        Assert.Equal(2, back.ExpiringNotifiedDays);
    }

    /// <summary>
    /// A version 8 database with held messages is brought up to date, and the held messages table, which Notifications
    /// keeps in its own file, is dropped.
    /// True: an upgrade opens, and leaves no table nothing reads.
    /// False: an older database would fail to open, or keep a table of stale messages.
    /// </summary>
    [Fact]
    public void Open_VersionEight_DropsTheHeldMessages()
    {
        _db.Dispose();
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "v8.db");
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + path))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            // The version 8 tables, the held messages without OneDevice, each with a row.
            cmd.CommandText = @"CREATE TABLE Requests (Id TEXT PRIMARY KEY, Type INTEGER NOT NULL, Title TEXT NOT NULL, Year INTEGER, Overview TEXT, PosterUrl TEXT,
                ProviderIds TEXT NOT NULL, Seasons TEXT, UserId TEXT NOT NULL, UserName TEXT NOT NULL, RequesterNote TEXT, CreatedUtc TEXT NOT NULL, UpdatedUtc TEXT NOT NULL,
                Status INTEGER NOT NULL, Note TEXT, DecidedBy TEXT, LibraryItemId TEXT, AvailableSeasons TEXT, ArrId INTEGER, Progress REAL, ProgressText TEXT,
                SendFailures INTEGER NOT NULL DEFAULT 0, RetryAfterUtc TEXT, BackdropUrl TEXT, ExpectedEpisodes TEXT, ServerId TEXT, ProfileId TEXT, QuotaCost INTEGER,
                RemovedFromArr INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE HeldMessages (Id TEXT PRIMARY KEY, RequestId TEXT NOT NULL, UserId TEXT NOT NULL, Header TEXT NOT NULL, Text TEXT NOT NULL, CreatedUtc TEXT NOT NULL);
                INSERT INTO Requests (Id, Type, Title, ProviderIds, UserId, UserName, CreatedUtc, UpdatedUtc, Status)
                VALUES ('0123456789abcdef0123456789abcdef', 0, 'Old', '{}', 'fedcba9876543210fedcba9876543210', 'viewer', '2030-01-01T00:00:00.0000000Z', '2030-01-01T00:00:00.0000000Z', 0);
                INSERT INTO HeldMessages VALUES ('11111111111111111111111111111111', '0123456789abcdef0123456789abcdef', 'fedcba9876543210fedcba9876543210', 'Approved: Old', '', '2030-01-01T00:00:00.0000000Z');";
            cmd.ExecuteNonQuery();
            DatabaseSchema.Stamp(connection, 8, 4);
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var migrated = new RequestDatabase(path, NullLogger<RequestDatabase>.Instance);
        var old = migrated.Get(Guid.Parse("0123456789abcdef0123456789abcdef"))!;
        Assert.Null(old.AgingNotifiedUtc);
        Assert.Null(old.ExpiringNotifiedDays);
        migrated.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var check = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + path + ";Pooling=False");
        check.Open();
        using var tables = check.CreateCommand();
        tables.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('HeldMessages', 'PushSubscriptions')";
        Assert.Equal(0L, (long)tables.ExecuteScalar()!);
        Assert.Equal(19, DatabaseSchema.GetVersion(check));
    }

    /// <summary>
    /// A page filters by requester, and requesters are listed under the name on their latest request.
    /// True: the queue's requester filter shows each person once by their current name.
    /// False: a renamed user would be listed twice, or the filter would miss their requests.
    /// </summary>
    [Fact]
    public void Requesters_Names_AreTheirLatest()
    {
        var anna = Guid.NewGuid();
        var ben = Guid.NewGuid();
        MediaRequest Of(string title, Guid user, string name, DateTime created)
        {
            var r = Make(title, user: user, created: created);
            r.UserName = name;
            return r;
        }

        // Names change: a requester is listed under the name on their latest request.
        _db.Insert(Of("Dune", anna, "anna.old", DateTime.UtcNow.AddDays(-2)));
        _db.Insert(Of("Arrival", anna, "anna", DateTime.UtcNow));
        _db.Insert(Of("Up", ben, "ben", DateTime.UtcNow));

        var page = _db.Page(new PagedQuery(), anna);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(new[] { "Arrival", "Dune" }, page.Items.Select(r => r.Title));
        Assert.Equal(3, _db.Page(new PagedQuery()).TotalCount);

        var requesters = _db.Requesters();
        Assert.Equal(new[] { 2, 1 }, requesters.Select(r => r.Count));
        Assert.Equal(new[] { "anna", "ben" }, requesters.Select(r => r.UserName));
    }

    /// <summary>
    /// A page sorts by standing, each status by title, and can leave out a type, one user, and dropped requests.
    /// True: the browse view lists what is waiting first.
    /// False: requests would show in the wrong order, or the filters would not hold.
    /// </summary>
    [Fact]
    public void Page_ByStatus_SortsByStandingAndLeavesOutOthers()
    {
        var me = Guid.NewGuid();
        _db.Insert(Make("Zodiac", RequestStatus.Approved));
        _db.Insert(Make("Alien", RequestStatus.Available));
        _db.Insert(Make("Brazil", RequestStatus.Pending));
        _db.Insert(Make("Andor", RequestStatus.Approved, type: RequestType.Series));
        _db.Insert(Make("Mine", RequestStatus.Failed, me));
        var dropped = Make("Dropped", RequestStatus.Approved);
        dropped.ArrId = 4;
        dropped.RemovedFromArr = true;
        _db.Insert(dropped);

        // Waiting first, then failed, approved, partly in, in the library and rejected; each status by title.
        Assert.Equal(new[] { "Brazil", "Mine", "Andor", "Dropped", "Zodiac", "Alien" }, _db.Page(new PagedQuery { Take = 50 }, byStatus: true).Items.Select(r => r.Title));
        Assert.Equal(new[] { "Brazil", "Zodiac", "Alien" },
            _db.Page(new PagedQuery { Take = 50 }, exceptUserId: me, byStatus: true, type: RequestType.Movie, liveOnly: true).Items.Select(r => r.Title));
    }

    /// <summary>
    /// A version 1 database is brought up to date, its rows kept and its new columns usable.
    /// True: a server upgraded from the first release keeps its requests.
    /// False: the upgrade would fail or lose the old requests.
    /// </summary>
    [Fact]
    public void Open_VersionOne_IsBroughtUpToDate()
    {
        _db.Dispose();
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "v1.db");
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + path))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            // The version 1 table, stamped as version 1, with one row.
            cmd.CommandText = @"CREATE TABLE Requests (Id TEXT PRIMARY KEY, Kind INTEGER NOT NULL, Title TEXT NOT NULL, Year INTEGER, Overview TEXT, PosterUrl TEXT,
                ProviderIds TEXT NOT NULL, Seasons TEXT, UserId TEXT NOT NULL, UserName TEXT NOT NULL, RequesterNote TEXT, CreatedUtc TEXT NOT NULL, UpdatedUtc TEXT NOT NULL,
                Status INTEGER NOT NULL, Note TEXT, DecidedBy TEXT, LibraryItemId TEXT, AvailableSeasons TEXT, ArrId INTEGER, Progress REAL, ProgressText TEXT,
                SendFailures INTEGER NOT NULL DEFAULT 0, RetryAfterUtc TEXT);
                INSERT INTO Requests (Id, Kind, Title, ProviderIds, UserId, UserName, CreatedUtc, UpdatedUtc, Status)
                VALUES ('0123456789abcdef0123456789abcdef', 1, 'Old', '{}', 'fedcba9876543210fedcba9876543210', 'viewer', '2030-01-01T00:00:00.0000000Z', '2030-01-01T00:00:00.0000000Z', 0);";
            cmd.ExecuteNonQuery();
            DatabaseSchema.Stamp(connection, 1, 1);
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var migrated = new RequestDatabase(path, NullLogger<RequestDatabase>.Instance);
        var old = migrated.Get(Guid.Parse("0123456789abcdef0123456789abcdef"))!;
        Assert.Equal("Old", old.Title);
        Assert.Equal(RequestType.Series, old.Type);
        Assert.Null(old.BackdropUrl);
        old.BackdropUrl = "https://image.tmdb.org/t/p/w1280/b.jpg";
        old.ExpectedEpisodes = new Dictionary<int, int> { [1] = 10, [2] = 8 };
        migrated.Update(old);
        var back = migrated.Get(old.Id)!;
        Assert.Equal(old.BackdropUrl, back.BackdropUrl);
        Assert.Equal(8, back.ExpectedEpisodes![2]);
    }

    /// <summary>
    /// Cleanup keeps requests a quota window still counts, past the retention.
    /// True: a short retention cannot reset anyone's quota.
    /// False: a requester could clean their quota away by waiting out the retention.
    /// </summary>
    [Fact]
    public void DeleteClosedBefore_QuotaWindow_KeepsRequestsItCounts()
    {
        var recent = Make("Recent", RequestStatus.Available, created: DateTime.UtcNow.AddDays(-5));
        recent.UpdatedUtc = DateTime.UtcNow.AddDays(-4);
        var old = Make("Old", RequestStatus.Available, created: DateTime.UtcNow.AddDays(-60));
        old.UpdatedUtc = DateTime.UtcNow.AddDays(-50);
        _db.Insert(recent);
        _db.Insert(old);

        // Retention of two days, with a thirty-day quota window: only the request outside the window goes.
        Assert.Single(_db.DeleteClosedBefore(DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(-30)));
        Assert.NotNull(_db.Get(recent.Id));
        Assert.Null(_db.Get(old.Id));
    }

    /// <summary>
    /// A damaged file is set aside and a new database started.
    /// True: a broken file never stops Requests from working.
    /// False: the plugin would fail to start on a damaged file.
    /// </summary>
    [Fact]
    public void Open_DamagedFile_IsSetAsideAndStartedAgain()
    {
        _db.Dispose();
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "damaged.db");
        File.WriteAllText(path, "this is not a database, just text that is long enough to fill a header page of a file");
        using var fresh = new RequestDatabase(path, NullLogger<RequestDatabase>.Instance);
        var r = Make("After");
        fresh.Insert(r);
        Assert.Equal("After", fresh.Get(r.Id)!.Title);
        Assert.Contains(Directory.GetFiles(_dir), f => Path.GetFileName(f).StartsWith("damaged.db.", StringComparison.Ordinal) || Path.GetFileName(f).Contains("corrupt", StringComparison.Ordinal));
    }

    /// <summary>
    /// An existing database is opened again with its requests.
    /// True: requests survive a restart.
    /// False: a restart would start an empty database.
    /// </summary>
    [Fact]
    public void Open_ExistingFile_KeepsItsRequests()
    {
        var r = Make("Persisted");
        _db.Insert(r);
        _db.Dispose();
        using var again = new RequestDatabase(Path.Combine(_dir, "requests.db"), NullLogger<RequestDatabase>.Instance);
        Assert.Equal("Persisted", again.Get(r.Id)!.Title);
        Assert.Equal(1, again.CountByStatus()[RequestStatus.Pending]);
    }

    /// <summary>
    /// The requests a library item fulfilled are found, without those an administrator marked available.
    /// True: a removed item rechecks only the requests it fulfilled.
    /// False: a removal would reopen requests it never fulfilled.
    /// </summary>
    [Fact]
    public void FulfilledBy_LibraryItems_FindsOnlyFulfilledRequests()
    {
        var item = Guid.NewGuid();
        var other = Guid.NewGuid();
        var available = Make("Available", RequestStatus.Available);
        available.LibraryItemId = item;
        var partial = Make("Partial", RequestStatus.PartiallyAvailable, type: RequestType.Series);
        partial.LibraryItemId = other;
        var marked = Make("Marked", RequestStatus.Available);
        marked.LibraryItemId = item;
        marked.MarkedAvailable = true;
        var sent = Make("Sent", RequestStatus.Approved);
        sent.LibraryItemId = item;
        var elsewhere = Make("Elsewhere", RequestStatus.Available);
        elsewhere.LibraryItemId = Guid.NewGuid();
        foreach (var r in new[] { available, partial, marked, sent, elsewhere, Make("Unmatched", RequestStatus.Available) })
        {
            _db.Insert(r);
        }

        // Only fulfilled ones, and not those an administrator marked available.
        Assert.Equal(new[] { "Available" }, _db.FulfilledBy(new[] { item }).Select(r => r.Title));
        Assert.Equal(new[] { "Available", "Partial" }, _db.FulfilledBy(new[] { item, other, item }).Select(r => r.Title).Order(StringComparer.Ordinal));
        Assert.Empty(_db.FulfilledBy(new[] { Guid.NewGuid() }));
        Assert.Empty(_db.FulfilledBy(Array.Empty<Guid>()));
    }

    /// <summary>
    /// The open requests stay the same list until a request is written, and a failed Atomic writes nothing.
    /// True: reads that change nothing never rebuild the list, and a failure leaves no half write.
    /// False: every read would rebuild the list, or a failed write would leave a request behind.
    /// </summary>
    [Fact]
    public void OpenRequests_UntilARequestIsWritten_StayTheSameList()
    {
        var waiting = Make("Waiting");
        _db.Insert(waiting);
        var open = _db.OpenRequests();

        // A delete that found nothing and an Atomic that only read change no request.
        Assert.False(_db.Delete(Guid.NewGuid()));
        Assert.Empty(_db.DeleteClosedBefore(DateTime.UtcNow.AddDays(-1)));
        _db.Atomic(() => _db.Get(waiting.Id));
        Assert.Same(open, _db.OpenRequests());

        // One that failed part way wrote nothing.
        Assert.Throws<InvalidOperationException>(() => _db.Atomic<bool>(() =>
        {
            _db.Insert(Make("Rolled Back"));
            Assert.Equal(2, _db.OpenRequests().Count);
            throw new InvalidOperationException("stop");
        }));
        Assert.Same(open, _db.OpenRequests());
        Assert.Single(_db.List());

        _db.Insert(Make("Another"));
        var after = _db.OpenRequests();
        Assert.NotSame(open, after);
        Assert.Equal(2, after.Count);
        Assert.Same(after, _db.OpenRequests());
    }

    /// <summary>
    /// Reads on another thread do not wait for an Atomic, and see only what was committed.
    /// True: pages stay quick while a long write runs.
    /// False: a page would hang behind a write, or see an uncommitted request.
    /// </summary>
    [Fact]
    public void Get_DuringAnAtomic_DoesNotWait()
    {
        var kept = Make("Kept");
        _db.Insert(kept);
        var added = Make("Added");
        _db.Atomic(() =>
        {
            _db.Insert(added);

            // Inside, its own write is seen; on another thread, only what was committed, without waiting for this to end.
            Assert.NotNull(_db.Get(added.Id));
            var outside = Task.Run(() => (_db.Get(kept.Id), _db.Get(added.Id), _db.Page(new PagedQuery()).TotalCount));
            Assert.True(outside.Wait(TimeSpan.FromSeconds(10)));
            Assert.NotNull(outside.Result.Item1);
            Assert.Null(outside.Result.Item2);
            Assert.Equal(1, outside.Result.Item3);
            return true;
        });

        Assert.NotNull(_db.Get(added.Id));
    }

    /// <summary>
    /// A version 14 database gains the indexes its queries use.
    /// True: the queue's queries stay fast on large databases.
    /// False: a large database would be read in full for each page.
    /// </summary>
    [Fact]
    public void Open_VersionFourteen_GainsTheIndexesItsQueriesUse()
    {
        var r = Make("Before");
        r.Status = RequestStatus.Available;
        r.LibraryItemId = Guid.NewGuid();
        _db.Insert(r);
        _db.Dispose();
        var path = Path.Combine(_dir, "requests.db");
        string Plan(Microsoft.Data.Sqlite.SqliteConnection connection, string sql)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "EXPLAIN QUERY PLAN " + sql;
            using var reader = cmd.ExecuteReader();
            var plan = new List<string>();
            while (reader.Read())
            {
                plan.Add(reader.GetString(3));
            }

            return string.Join(" | ", plan);
        }

        List<string> Indexes(Microsoft.Data.Sqlite.SqliteConnection connection)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'Requests' AND name LIKE 'idx_%'";
            using var reader = cmd.ExecuteReader();
            var names = new List<string>();
            while (reader.Read())
            {
                names.Add(reader.GetString(0));
            }

            return names;
        }

        // Taken back to version 14's indexes and stamp.
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + path + ";Pooling=False"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"DROP INDEX idx_requests_status_created; DROP INDEX idx_requests_created; DROP INDEX idx_requests_library; DROP INDEX idx_requests_user_counts;
                CREATE INDEX idx_requests_status ON Requests(Status); ALTER TABLE Requests DROP COLUMN Specials;";
            cmd.ExecuteNonQuery();
            DatabaseSchema.Stamp(connection, 14, 4);
            Assert.Equal(new[] { "idx_requests_status", "idx_requests_user" }, Indexes(connection).Order(StringComparer.Ordinal));
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using (var migrated = new RequestDatabase(path, NullLogger<RequestDatabase>.Instance))
        {
            Assert.Equal("Before", Assert.Single(migrated.FulfilledBy(new[] { r.LibraryItemId!.Value })).Title);
        }

        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + path + ";Pooling=False"))
        {
            connection.Open();
            Assert.Equal(19, DatabaseSchema.GetVersion(connection));
            Assert.Equal(
                new[] { "idx_requests_created", "idx_requests_library", "idx_requests_status_created", "idx_requests_user", "idx_requests_user_counts" },
                Indexes(connection).Order(StringComparer.Ordinal));

            // The queries they're for use them.
            Assert.Contains("idx_requests_library", Plan(connection, "SELECT Id FROM Requests WHERE LibraryItemId IN ('a', 'b') AND Status IN (3, 5) AND MarkedAvailable = 0"), StringComparison.Ordinal);
            Assert.Contains("idx_requests_status_created", Plan(connection, "SELECT Id FROM Requests WHERE Status IN (0, 1, 4, 5) ORDER BY CreatedUtc DESC"), StringComparison.Ordinal);
            Assert.Contains("idx_requests_created", Plan(connection, "SELECT Id FROM Requests ORDER BY CreatedUtc DESC LIMIT 50"), StringComparison.Ordinal);
            Assert.Contains("COVERING INDEX idx_requests_user_counts", Plan(connection, "SELECT UserId, MAX(UserName), Type, Status, COUNT(*) FROM Requests GROUP BY UserId, Type, Status"), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A version 17 database gains the issues table, which keeps every field of an issue.
    /// True: issues can be reported after an upgrade.
    /// False: reporting an issue would fail after an upgrade.
    /// </summary>
    [Fact]
    public void Open_VersionSeventeen_GainsTheIssuesTable()
    {
        var r = Make("Before");
        _db.Insert(r);
        _db.Dispose();
        var path = Path.Combine(_dir, "requests.db");
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + path + ";Pooling=False"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DROP TABLE Issues";
            cmd.ExecuteNonQuery();
            DatabaseSchema.Stamp(connection, 17, 4);
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using (var migrated = new RequestDatabase(path, NullLogger<RequestDatabase>.Instance))
        {
            Assert.Equal("Before", migrated.Get(r.Id)!.Title);
            var issue = new Issue
            {
                Id = Guid.NewGuid(), ItemId = Guid.NewGuid(), ItemType = "Episode", Title = "Pilot", SeriesName = "Tidewater Station", SeasonNumber = 1, EpisodeNumber = 2,
                Path = "/media/shows/a.mkv", FileSize = 10, FileModifiedUtc = DateTime.UtcNow, ProviderIds = new() { ["Tvdb"] = "1" }, SeriesProviderIds = new() { ["Tvdb"] = "2" },
                CreatedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow,
                Reporters = { new IssueReporter { UserId = r.UserId, UserName = "viewer", Type = IssueType.Subtitles, ReportedUtc = DateTime.UtcNow } },
                Notes = { new IssueNote { UserId = r.UserId, User = "viewer", Text = "Out of sync – by two seconds", Time = DateTime.UtcNow } }
            };
            migrated.InsertIssue(issue);
            var back = migrated.GetIssue(issue.Id)!;
            Assert.Equal(("Tidewater Station", 1, 2, "2"), (back.SeriesName, back.SeasonNumber, back.EpisodeNumber, back.SeriesProviderIds!["Tvdb"]));
            Assert.Equal(IssueType.Subtitles, Assert.Single(back.Reporters).Type);
            Assert.Equal("Out of sync – by two seconds", Assert.Single(back.Notes).Text);
            Assert.Equal(issue.Id, Assert.Single(migrated.IssuesForItem(issue.ItemId)).Id);
            Assert.Equal(1, migrated.CountReportsSince(r.UserId, DateTime.UtcNow.AddDays(-1)));
        }

        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + path + ";Pooling=False"))
        {
            connection.Open();
            Assert.Equal(19, DatabaseSchema.GetVersion(connection));
        }
    }
}
