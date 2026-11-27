using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using Siphonophore.Stolon;
using MediaBrowser.Common.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable CA2100 // SQL commands use only internal constants and parameterized values

namespace Jellyfin.Plugin.Necto.Services.Requests;

/// <summary>
/// Stores requests in a SQLite database in the server's data folder. Writes share one connection under a lock,
/// which also lets callers make a check and a write atomic with <see cref="Atomic{T}"/>; reads outside one take a
/// pooled connection of their own, so a page or a list never waits behind a write.
/// </summary>
[PluginService(ServiceLifetime.Singleton)]
public sealed partial class RequestDatabase : IDisposable
{
    // 2 added BackdropUrl, 3 ExpectedEpisodes, 5 the held messages, 6 ServerId and ProfileId, 7 QuotaCost, 8 RemovedFromArr,
    // 9 AgingNotifiedUtc, ExpiringNotifiedDays and the held messages' OneDevice, 10 DecidedUtc, 11 Joined, 12 SeasonProgress, 13 Genres,
    // 14 MarkedAvailable and DownloadDoneUtc, 15 indexes for the library item, the date and the per-requester counts,
    // 16 Specials, 17 the browsers signed up for notifications, 18 the Issues table, 19 drops the held messages and the
    // browsers, which Notifications keeps.
    // Older builds name their columns, so they can still read what this one writes.
    private const int SchemaVersion = 19;

    // Version 4 renamed the Kind column, so older builds can't read it.
    private const int MinReaderSchemaVersion = 4;

    // Genres kept as written, accents and all, so filtering by one can match its name in the stored list.
    private static readonly JsonSerializerOptions GenreJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // Column order must match Read.
    private const string Columns = "Id, Type, Title, Year, Overview, PosterUrl, ProviderIds, Seasons, UserId, UserName, RequesterNote, "
        + "CreatedUtc, UpdatedUtc, Status, Note, DecidedBy, LibraryItemId, AvailableSeasons, ArrId, Progress, ProgressText, SendFailures, RetryAfterUtc, BackdropUrl, ExpectedEpisodes, ServerId, ProfileId, QuotaCost, RemovedFromArr, "
        + "AgingNotifiedUtc, ExpiringNotifiedDays, DecidedUtc, Joined, SeasonProgress, Genres, MarkedAvailable, DownloadDoneUtc, Specials";

    private const string Schema = @"
        CREATE TABLE IF NOT EXISTS Requests (
            Id TEXT PRIMARY KEY,
            Type INTEGER NOT NULL,
            Title TEXT NOT NULL COLLATE NOCASE,
            Year INTEGER,
            Overview TEXT,
            PosterUrl TEXT,
            ProviderIds TEXT NOT NULL,
            Seasons TEXT,
            UserId TEXT NOT NULL,
            UserName TEXT NOT NULL COLLATE NOCASE,
            RequesterNote TEXT,
            CreatedUtc TEXT NOT NULL,
            UpdatedUtc TEXT NOT NULL,
            Status INTEGER NOT NULL,
            Note TEXT,
            DecidedBy TEXT,
            LibraryItemId TEXT,
            AvailableSeasons TEXT,
            ArrId INTEGER,
            Progress REAL,
            ProgressText TEXT,
            SendFailures INTEGER NOT NULL DEFAULT 0,
            RetryAfterUtc TEXT,
            BackdropUrl TEXT,
            ExpectedEpisodes TEXT,
            ServerId TEXT,
            ProfileId TEXT,
            QuotaCost INTEGER,
            RemovedFromArr INTEGER NOT NULL DEFAULT 0,
            AgingNotifiedUtc TEXT,
            ExpiringNotifiedDays INTEGER,
            DecidedUtc TEXT,
            Joined TEXT,
            SeasonProgress TEXT,
            Genres TEXT,
            MarkedAvailable INTEGER NOT NULL DEFAULT 0,
            DownloadDoneUtc TEXT,
            Specials TEXT
        );
        CREATE INDEX IF NOT EXISTS idx_requests_user ON Requests(UserId, CreatedUtc);" + Indexes + IssuesSchema;

    // Lists by status come newest first, as do pages without a status; the library item finds what a removed title
    // fulfilled; the per-requester counts read the index alone, the name included.
    private const string Indexes = @"
        CREATE INDEX IF NOT EXISTS idx_requests_status_created ON Requests(Status, CreatedUtc);
        CREATE INDEX IF NOT EXISTS idx_requests_created ON Requests(CreatedUtc);
        CREATE INDEX IF NOT EXISTS idx_requests_library ON Requests(LibraryItemId);
        CREATE INDEX IF NOT EXISTS idx_requests_user_counts ON Requests(UserId, Type, Status, UserName);";

    private readonly ILogger<RequestDatabase> _logger;
    private readonly string _path;

    // Reads open their own connection from a pool kept for this file; the writer's is never pooled.
    private readonly string _readConnectionString;

    // Held for every write, and for the whole of an Atomic.
    private readonly object _lock = new();

    // Held while the open requests are read again, so callers that find them out of date together read them once.
    private readonly object _openLock = new();
    private SqliteConnection? _connection;

    // The transaction of the Atomic under way, how deeply Atomics are nested in it, and whether it changed any request.
    private SqliteTransaction? _transaction;
    private int _depth;
    private bool _changedRequests;

    // Set once the writer has opened the file and brought its schema up to date, which reads wait for the first time.
    private volatile bool _ready;
    private volatile bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestDatabase"/> class.
    /// </summary>
    /// <param name="paths">The application paths, used to place the file.</param>
    /// <param name="logger">The logger.</param>
    public RequestDatabase(IApplicationPaths paths, ILogger<RequestDatabase> logger)
        : this(Path.Join(paths?.DataPath ?? throw new ArgumentNullException(nameof(paths)), "requests", "requests.db"), logger)
    {
    }

    internal RequestDatabase(string path, ILogger<RequestDatabase> logger)
    {
        _path = path;
        _logger = logger;
        _readConnectionString = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWrite, Pooling = true }.ToString();
    }

    /// <summary>
    /// Runs <paramref name="work"/> holding the write lock in one transaction, so a read and the write it decides on
    /// cannot interleave with another caller's, and a failure part way leaves nothing half written. Reads inside it see
    /// its own writes. Calls on this database from inside it are fine; the lock is reentrant, and a nested call joins
    /// the outer transaction.
    /// </summary>
    /// <typeparam name="T">What the work returns.</typeparam>
    /// <param name="work">The work.</param>
    /// <returns>Its result.</returns>
    public T Atomic<T>(Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        lock (_lock)
        {
            if (_depth > 0)
            {
                _depth++;
                try
                {
                    return work();
                }
                finally
                {
                    _depth--;
                }
            }

            // Immediate, so the write lock is taken in the file at the start rather than at the first write.
            var transaction = Connection.BeginTransaction(deferred: false);
            (_transaction, _depth, _changedRequests) = (transaction, 1, false);
            try
            {
                var result = work();
                transaction.Commit();

                // Counted once it's committed, still under the lock, so open requests read after the count include it.
                if (_changedRequests)
                {
                    Interlocked.Increment(ref _writes);
                }

                return result;
            }
            catch
            {
                try
                {
                    transaction.Rollback();
                }
                catch (SqliteException ex)
                {
                    _logger.LogWarning(ex, "Rolling back a requests database transaction failed");
                }

                throw;
            }
            finally
            {
                transaction.Dispose();
                (_transaction, _depth, _changedRequests) = (null, 0, false);
            }
        }
    }

    /// <summary>Returns a request by id, or null.</summary>
    /// <param name="id">The request id.</param>
    /// <returns>The request, or null.</returns>
    public MediaRequest? Get(Guid id)
        => Select("WHERE Id = @id", p => p.AddWithValue("@id", Key(id))).FirstOrDefault();

    /// <summary>Adds a request.</summary>
    /// <param name="request">The request.</param>
    public void Insert(MediaRequest request) => Write(request, "INSERT INTO Requests (" + Columns + ") VALUES (" + Parameters() + ")");

    /// <summary>Writes every field of an existing request.</summary>
    /// <param name="request">The request.</param>
    public void Update(MediaRequest request)
    {
        var sets = string.Join(", ", ColumnNames().Skip(1).Select(c => c + " = @" + c));
        Write(request, "UPDATE Requests SET " + sets + " WHERE Id = @Id");
    }

    /// <summary>Removes a request.</summary>
    /// <param name="id">The request id.</param>
    /// <returns>True when there was one.</returns>
    public bool Delete(Guid id)
        => Execute("DELETE FROM Requests WHERE Id = @id", p => p.AddWithValue("@id", Key(id)), requests: true) > 0;

    /// <summary>Returns requests, newest first.</summary>
    /// <param name="userId">Only this user's, when set.</param>
    /// <param name="statuses">Only these statuses, when set.</param>
    /// <returns>The requests.</returns>
    public List<MediaRequest> List(Guid? userId = null, IReadOnlyCollection<RequestStatus>? statuses = null)
    {
        var where = new List<string>();
        if (userId.HasValue)
        {
            where.Add("UserId = @user");
        }

        if (statuses is { Count: > 0 })
        {
            where.Add("Status IN (" + string.Join(", ", statuses.Select(Number)) + ")");
        }

        return Select(
            (where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : string.Empty) + " ORDER BY CreatedUtc DESC",
            p =>
            {
                if (userId.HasValue)
                {
                    p.AddWithValue("@user", Key(userId.Value));
                }
            });
    }

    /// <summary>Counts a requester's requests that hold a place (open, and not dropped by Radarr or Sonarr).</summary>
    /// <param name="userId">The requester.</param>
    /// <returns>How many.</returns>
    public int CountHoldingAPlace(Guid userId)
        => Convert.ToInt32(
            Scalar(
                "SELECT COUNT(*) FROM Requests WHERE UserId = @user AND RemovedFromArr = 0 AND Status IN (" + string.Join(", ", RequestService.OpenStatuses.Select(Number)) + ")",
                p => p.AddWithValue("@user", Key(userId))),
            CultureInfo.InvariantCulture);

    /// <summary>
    /// Returns one page of requests for the dashboard table, newest first. The filter is a status name, or
    /// <c>Open</c> for every request still waiting on something; the search matches title, requester and note.
    /// </summary>
    /// <param name="query">The page, search and filter.</param>
    /// <param name="userId">Only this requester's, when set.</param>
    /// <param name="exceptUserId">Everyone's but this requester's, when set.</param>
    /// <param name="byStatus">Ordered by where they stand (<see cref="StatusOrder"/>), then by title, instead of newest first.</param>
    /// <param name="type">Only this type's, when set.</param>
    /// <param name="liveOnly">Leaves out those Radarr or Sonarr dropped after they were sent.</param>
    /// <param name="withJoined">With a requester, also the requests they joined.</param>
    /// <param name="genres">Only those with one of these genres, when set.</param>
    /// <param name="withScheduled">With a requester, also everyone's scheduled ones (<see cref="ScheduledStatuses"/>, not dropped), of this type, or either for <see langword="null"/>.</param>
    /// <returns>The page.</returns>
    public PagedResult<MediaRequest> Page(PagedQuery query, Guid? userId = null, Guid? exceptUserId = null, bool byStatus = false, RequestType? type = null, bool liveOnly = false, bool withJoined = false, IReadOnlyCollection<string>? genres = null, (bool On, RequestType? Type) withScheduled = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var where = new List<string>();
        if (type is { } only)
        {
            where.Add("Type = " + ((int)only).ToString(CultureInfo.InvariantCulture));
        }

        if (liveOnly)
        {
            where.Add("RemovedFromArr = 0");
        }

        if (userId is not null)
        {
            // Joined holds each joiner's id as JSON writes a Guid, which is how it's matched.
            var own = withJoined ? "UserId = @user OR Joined LIKE @joined" : "UserId = @user";
            if (withScheduled.On)
            {
                // Everyone's scheduled ones too, as Requests/Scheduled lists them.
                own += " OR (Status IN (" + string.Join(", ", ScheduledStatuses.Select(Number)) + ") AND RemovedFromArr = 0"
                    + (withScheduled.Type is { } scheduledType ? " AND Type = " + ((int)scheduledType).ToString(CultureInfo.InvariantCulture) : string.Empty) + ")";
            }

            where.Add("(" + own + ")");
        }

        if (exceptUserId is not null)
        {
            where.Add("UserId <> @except");
        }

        // Genres are kept as a JSON list of names, matched by name, in quotes so one can't match part of another.
        var genreNames = (genres ?? Array.Empty<string>()).Where(g => !string.IsNullOrWhiteSpace(g)).ToList();
        if (genreNames.Count > 0)
        {
            where.Add("(" + string.Join(" OR ", genreNames.Select((_, i) => "Genres LIKE @genre" + i.ToString(CultureInfo.InvariantCulture) + " ESCAPE '\\'")) + ")");
        }

        // Open, or one or more status names separated by commas, such as Pending,Failed for a page's group.
        var statuses = string.Equals(query.Filter, "Open", StringComparison.OrdinalIgnoreCase)
            ? RequestService.OpenStatuses.ToList()
            : (query.Filter ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(f => Enum.TryParse<RequestStatus>(f, true, out var parsed) && Enum.IsDefined(parsed) ? parsed : (RequestStatus?)null)
                .Where(f => f.HasValue)
                .Select(f => f!.Value)
                .Distinct()
                .ToList();
        if (statuses.Count > 0)
        {
            where.Add("Status IN (" + string.Join(", ", statuses.Select((_, i) => "@status" + i.ToString(CultureInfo.InvariantCulture))) + ")");
        }

        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            where.Add("(Title LIKE @q ESCAPE '\\' OR UserName LIKE @q ESCAPE '\\' OR RequesterNote LIKE @q ESCAPE '\\')");
        }

        var clause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : string.Empty;
        var (skip, take) = Window(query);
        void Bind(SqliteParameterCollection p)
        {
            p.AddWithValue("@take", take);
            p.AddWithValue("@skip", skip);
            for (var i = 0; i < genreNames.Count; i++)
            {
                p.AddWithValue("@genre" + i.ToString(CultureInfo.InvariantCulture), "%" + JsonSerializer.Serialize(genreNames[i].Trim(), GenreJson).Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%");
            }

            if (userId is { } user)
            {
                p.AddWithValue("@user", Key(user));
                if (withJoined)
                {
                    p.AddWithValue("@joined", "%\"UserId\":\"" + user.ToString("D", CultureInfo.InvariantCulture) + "\"%");
                }
            }

            if (exceptUserId is { } except)
            {
                p.AddWithValue("@except", Key(except));
            }

            for (var i = 0; i < statuses.Count; i++)
            {
                p.AddWithValue("@status" + i.ToString(CultureInfo.InvariantCulture), (int)statuses[i]);
            }

            if (!string.IsNullOrEmpty(search))
            {
                p.AddWithValue("@q", "%" + search.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%");
            }
        }

        // The count and the rows are read in one transaction, so a write between them can't make them disagree.
        var order = byStatus
            ? "CASE Status " + string.Concat(StatusOrder.Select((s, i) => " WHEN " + Number(s) + " THEN " + i.ToString(CultureInfo.InvariantCulture))) + " END, Title COLLATE NOCASE, CreatedUtc DESC"
            : "CreatedUtc DESC";
        return Query(
            (connection, transaction) =>
            {
                var total = Convert.ToInt32(ScalarOn(connection, transaction, "SELECT COUNT(*) FROM Requests " + clause, Bind), CultureInfo.InvariantCulture);
                var items = SelectOn(connection, transaction, clause + " ORDER BY " + order + " LIMIT @take OFFSET @skip", Bind);
                return new PagedResult<MediaRequest>(items, total, skip, take);
            },
            together: true);
    }

    /// <summary>
    /// Where a request stands, in the order the request page lists them: waiting for approval first, then failed to
    /// send, approved, partly in the library, in the library, and rejected.
    /// </summary>
    internal static readonly RequestStatus[] StatusOrder =
    {
        RequestStatus.Pending, RequestStatus.Failed, RequestStatus.Approved, RequestStatus.PartiallyAvailable, RequestStatus.Available, RequestStatus.Declined
    };

    /// <summary>Approved, but not all in the library yet: approved, failed to send, or partly in the library.</summary>
    internal static readonly RequestStatus[] ScheduledStatuses = { RequestStatus.Approved, RequestStatus.Failed, RequestStatus.PartiallyAvailable };

    /// <summary>Returns everyone who has made requests, by name, with how many there are of theirs.</summary>
    /// <returns>The requesters.</returns>
    public List<Requester> Requesters() => Query((connection, transaction) =>
    {
        // The name on each person's latest request, as names can change.
        using var cmd = Command(
            connection,
            transaction,
            "SELECT UserId, (SELECT r2.UserName FROM Requests r2 WHERE r2.UserId = r.UserId ORDER BY r2.CreatedUtc DESC LIMIT 1), COUNT(*) "
                + "FROM Requests r GROUP BY UserId");
        using var reader = cmd.ExecuteReader();
        var list = new List<Requester>();
        while (reader.Read())
        {
            list.Add(new Requester(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetInt32(2)));
        }

        return list.OrderBy(r => r.UserName, StringComparer.OrdinalIgnoreCase).ToList();
    });

    /// <summary>Counts requests by requester, type and status.</summary>
    /// <returns>Each requester's counts, with the name their latest request has.</returns>
    /// <param name="userId">Only this requester's, when set.</param>
    public List<(Guid UserId, string UserName, RequestType Type, RequestStatus Status, int Count)> CountByUser(Guid? userId = null) => Query((connection, transaction) =>
    {
        using var cmd = Command(
            connection,
            transaction,
            "SELECT UserId, (SELECT r2.UserName FROM Requests r2 WHERE r2.UserId = r.UserId ORDER BY r2.CreatedUtc DESC LIMIT 1), Type, Status, COUNT(*) "
                + "FROM Requests r" + (userId is null ? string.Empty : " WHERE UserId = @user") + " GROUP BY UserId, Type, Status");
        if (userId is { } only)
        {
            cmd.Parameters.AddWithValue("@user", Key(only));
        }

        using var reader = cmd.ExecuteReader();
        var counts = new List<(Guid, string, RequestType, RequestStatus, int)>();
        while (reader.Read())
        {
            counts.Add((Guid.TryParse(reader.GetString(0), out var id) ? id : Guid.Empty, reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                (RequestType)reader.GetInt32(2), (RequestStatus)reader.GetInt32(3), reader.GetInt32(4)));
        }

        return counts;
    });

    /// <summary>Counts requests by status.</summary>
    /// <returns>The counts.</returns>
    public Dictionary<RequestStatus, int> CountByStatus() => Query((connection, transaction) =>
    {
        using var cmd = Command(connection, transaction, "SELECT Status, COUNT(*) FROM Requests GROUP BY Status");
        using var reader = cmd.ExecuteReader();
        var counts = new Dictionary<RequestStatus, int>();
        while (reader.Read())
        {
            counts[(RequestStatus)reader.GetInt32(0)] = reader.GetInt32(1);
        }

        return counts;
    });

    /// <summary>
    /// Returns a user's requests of one type made since <paramref name="sinceUtc"/>, oldest first, leaving
    /// declined ones out since they did not cost anything.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="type">The type.</param>
    /// <param name="sinceUtc">The start of the window.</param>
    /// <returns>The requests.</returns>
    public List<MediaRequest> RequestsSince(Guid userId, RequestType type, DateTime sinceUtc)
        => Select(
                "WHERE UserId = @user AND Type = @type AND Status <> " + Number(RequestStatus.Declined) + " AND CreatedUtc >= @since ORDER BY CreatedUtc",
                p =>
                {
                    p.AddWithValue("@user", Key(userId));
                    p.AddWithValue("@type", (int)type);
                    p.AddWithValue("@since", UtcTime.Format(sinceUtc));
                })
            .ToList();

    /// <summary>Returns requests whose genres aren't known yet, oldest first.</summary>
    /// <param name="limit">The most to return.</param>
    /// <returns>The requests.</returns>
    public List<MediaRequest> WithoutGenres(int limit)
        => Select("WHERE Genres IS NULL ORDER BY CreatedUtc LIMIT @limit", p => p.AddWithValue("@limit", limit)).ToList();

    /// <summary>
    /// Removes declined and available requests last changed before a time. Requests made after
    /// <paramref name="keepCreatedSinceUtc"/> are kept whatever their state, since quotas still count them.
    /// </summary>
    /// <param name="beforeUtc">The cut-off.</param>
    /// <param name="keepCreatedSinceUtc">Requests made since this time are kept.</param>
    /// <returns>The ids of the requests removed.</returns>
    public List<Guid> DeleteClosedBefore(DateTime beforeUtc, DateTime? keepCreatedSinceUtc = null) => Atomic(() =>
    {
        var closed = "Status IN (" + Number(RequestStatus.Declined) + ", " + Number(RequestStatus.Available) + ") AND UpdatedUtc < @before AND CreatedUtc < @created";
        void Bind(SqliteParameterCollection p)
        {
            p.AddWithValue("@before", UtcTime.Format(beforeUtc));
            p.AddWithValue("@created", UtcTime.Format(keepCreatedSinceUtc ?? DateTime.MaxValue.AddDays(-1)));
        }

        var ids = Query((connection, transaction) =>
        {
            using var cmd = Command(connection, transaction, "SELECT Id FROM Requests WHERE " + closed);
            Bind(cmd.Parameters);
            using var reader = cmd.ExecuteReader();
            var found = new List<Guid>();
            while (reader.Read())
            {
                found.Add(Guid.Parse(reader.GetString(0)));
            }

            return found;
        });
        if (ids.Count > 0)
        {
            Execute("DELETE FROM Requests WHERE " + closed, Bind, requests: true);
        }

        return ids;
    });

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _connection?.Dispose();
            _connection = null;
        }

        // The pooled read connections are closed too, so nothing keeps the file open.
        using var pooled = new SqliteConnection(_readConnectionString);
        SqliteConnection.ClearPool(pooled);
    }

    /// <summary>
    /// Gets the rows a page query asks for, as <see cref="Page"/> reads them: a negative skip from the start, and a
    /// take outside 1 to 200 as 50. Anything building a page from <see cref="Page"/>'s rows uses these too.
    /// </summary>
    /// <param name="query">The page query.</param>
    /// <returns>The rows to skip and take.</returns>
    internal static (int Skip, int Take) Window(PagedQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return (Math.Max(0, query.Skip), query.Take is > 0 and <= 200 ? query.Take : 50);
    }

    // A status as its column stores it, for SQL built from the enum rather than bare numbers.
    private static string Number(RequestStatus status) => ((int)status).ToString(CultureInfo.InvariantCulture);

    internal static string Key(Guid id) => id.ToString("N", CultureInfo.InvariantCulture);

    private static string[] ColumnNames() => Columns.Split(',', StringSplitOptions.TrimEntries);

    private static string Parameters() => string.Join(", ", ColumnNames().Select(c => "@" + c));

    private SqliteConnection Connection
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_connection is { State: ConnectionState.Open })
            {
                return _connection;
            }

            _connection?.Dispose();
            _connection = Open();
            _ready = true;
            return _connection;
        }
    }

    private SqliteConnection Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        for (var attempt = 0; ; attempt++)
        {
            // Built, not joined, so a data folder with a ; in its path still opens.
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString());
            SchemaAction action;
            try
            {
                connection.Open();
                using (var pragma = connection.CreateCommand())
                {
                    pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; PRAGMA synchronous=NORMAL;";
                    pragma.ExecuteNonQuery();
                }

                action = DatabaseSchema.Open(
                    connection,
                    SchemaVersion,
                    MinReaderSchemaVersion,
                    c => Run(c, Schema),
                    (c, from) => from < SchemaVersion && Migrate(c, from),
                    transactional: true);
            }
            catch (SqliteException ex) when (attempt == 0 && ex.SqliteErrorCode is 11 or 26)
            {
                // SQLITE_CORRUPT or SQLITE_NOTADB: a damaged file is kept aside and a fresh one started.
                _logger.LogError(ex, "The requests database at {Path} is damaged", _path);
                connection.Dispose();
                ClearReadPool();
                DatabaseSchema.SetAside(_path, "corrupt", 3, _logger);
                continue;
            }
            catch
            {
                // Anything else leaves the database closed; the next use tries again with a fresh connection.
                connection.Dispose();
                throw;
            }

            if (action is not (SchemaAction.TooNew or SchemaAction.MigrationFailed) || attempt > 0)
            {
                return connection;
            }

            // A database this build cannot use is kept aside rather than overwritten, and a fresh one started.
            connection.Dispose();
            ClearReadPool();
            DatabaseSchema.SetAside(_path, action.ToString(), 3, _logger);
        }
    }

    // Each schema version adds a step here, run in order from the stored version.
    private static bool Migrate(DbConnection connection, int from)
    {
        if (from < 1)
        {
            return false;
        }

        if (from < 2)
        {
            Run(connection, "ALTER TABLE Requests ADD COLUMN BackdropUrl TEXT");
        }

        if (from < 3)
        {
            Run(connection, "ALTER TABLE Requests ADD COLUMN ExpectedEpisodes TEXT");
        }

        if (from < 4)
        {
            Run(connection, "ALTER TABLE Requests RENAME COLUMN Kind TO Type");
        }

        if (from < 6)
        {
            Run(connection, "ALTER TABLE Requests ADD COLUMN ServerId TEXT");
            Run(connection, "ALTER TABLE Requests ADD COLUMN ProfileId TEXT");
        }

        if (from < 7)
        {
            Run(connection, "ALTER TABLE Requests ADD COLUMN QuotaCost INTEGER");
        }

        if (from < 8)
        {
            Run(connection, "ALTER TABLE Requests ADD COLUMN RemovedFromArr INTEGER NOT NULL DEFAULT 0");
        }

        if (from < 9)
        {
            Run(connection, "ALTER TABLE Requests ADD COLUMN AgingNotifiedUtc TEXT");
            Run(connection, "ALTER TABLE Requests ADD COLUMN ExpiringNotifiedDays INTEGER");
        }

        if (from < 10)
        {
            Run(connection, "ALTER TABLE Requests ADD COLUMN DecidedUtc TEXT");
        }

        if (from < 11)
        {
            Run(connection, "ALTER TABLE Requests ADD COLUMN Joined TEXT");
        }

        if (from < 12)
        {
            Run(connection, "ALTER TABLE Requests ADD COLUMN SeasonProgress TEXT");
        }

        if (from < 13)
        {
            Run(connection, "ALTER TABLE Requests ADD COLUMN Genres TEXT");
        }

        if (from < 14)
        {
            Run(connection, "ALTER TABLE Requests ADD COLUMN MarkedAvailable INTEGER NOT NULL DEFAULT 0");
            Run(connection, "ALTER TABLE Requests ADD COLUMN DownloadDoneUtc TEXT");
        }

        if (from < 15)
        {
            // The status and date index answers everything the status one did.
            Run(connection, "DROP INDEX IF EXISTS idx_requests_status");
            Run(connection, Indexes);
        }

        if (from < 16)
        {
            Run(connection, "ALTER TABLE Requests ADD COLUMN Specials TEXT");
        }

        if (from < 18)
        {
            Run(connection, IssuesSchema);
        }

        if (from < 19)
        {
            // Notifications keeps held messages and browsers in its own files, so these tables are never read.
            Run(connection, "DROP TABLE IF EXISTS HeldMessages; DROP TABLE IF EXISTS PushSubscriptions;");
        }

        return true;
    }

    private static void Run(DbConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    // Clears the pooled read connections, so none still points at a file that has been set aside.
    private void ClearReadPool()
    {
        using var pooled = new SqliteConnection(_readConnectionString);
        SqliteConnection.ClearPool(pooled);
    }

    // Runs a read. Inside an Atomic it uses the writer's connection and transaction, so it sees the Atomic's own writes
    // and stays consistent with them; anywhere else it takes a pooled connection and no lock, alongside any write.
    // Together reads everything in one transaction, for reads that must agree with each other.
    private T Query<T>(Func<SqliteConnection, SqliteTransaction?, T> read, bool together = false)
    {
        if (Monitor.IsEntered(_lock))
        {
            return read(Connection, _transaction);
        }

        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureReady();
        using var connection = new SqliteConnection(_readConnectionString);
        connection.Open();
        if (!together)
        {
            return read(connection, null);
        }

        using var transaction = connection.BeginTransaction(deferred: true);
        return read(connection, transaction);
    }

    // The first read waits for the writer to open the file and bring it up to date, setting a damaged one aside if
    // need be, so no read opens a file that's about to be replaced.
    private void EnsureReady()
    {
        if (!_ready)
        {
            lock (_lock)
            {
                _ = Connection;
            }
        }
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        return cmd;
    }

    private List<MediaRequest> Select(string clause, Action<SqliteParameterCollection>? bind)
        => Query((connection, transaction) => SelectOn(connection, transaction, clause, bind));

    private static List<MediaRequest> SelectOn(SqliteConnection connection, SqliteTransaction? transaction, string clause, Action<SqliteParameterCollection>? bind)
    {
        using var cmd = Command(connection, transaction, "SELECT " + Columns + " FROM Requests " + clause);
        bind?.Invoke(cmd.Parameters);
        using var reader = cmd.ExecuteReader();
        var list = new List<MediaRequest>();
        while (reader.Read())
        {
            list.Add(Read(reader));
        }

        return list;
    }

    private object? Scalar(string sql, Action<SqliteParameterCollection>? bind)
        => Query((connection, transaction) => ScalarOn(connection, transaction, sql, bind));

    private static object? ScalarOn(SqliteConnection connection, SqliteTransaction? transaction, string sql, Action<SqliteParameterCollection>? bind)
    {
        using var cmd = Command(connection, transaction, sql);
        bind?.Invoke(cmd.Parameters);
        return cmd.ExecuteScalar();
    }

    // Runs a write under the write lock, in the Atomic's transaction when one is under way. Requests is set for writes
    // to the Requests table, whose changed rows make the open requests out of date.
    private int Execute(string sql, Action<SqliteParameterCollection>? bind, bool requests = false)
    {
        lock (_lock)
        {
            using var cmd = Command(Connection, _transaction, sql);
            bind?.Invoke(cmd.Parameters);
            var changed = cmd.ExecuteNonQuery();
            if (requests && changed > 0)
            {
                // Inside an Atomic it's counted when the transaction commits; until then nothing else can see it.
                if (_transaction is null)
                {
                    Interlocked.Increment(ref _writes);
                }
                else
                {
                    _changedRequests = true;
                }
            }

            return changed;
        }
    }

    // How many committed writes have changed requests, and the open requests as read after the last of them, swapped whole.
    private long _writes;
    private OpenSnapshot? _open;

    /// <summary>
    /// Returns every open request (<see cref="RequestService.OpenStatuses"/>), read once until the next write that
    /// changes a request, for marking lists of titles: every list page and search reads them all. Until then every
    /// call returns the same list, so callers may key what they work out from it on the list itself. The requests are
    /// shared, so they're read-only to every caller.
    /// </summary>
    /// <returns>The open requests, newest first.</returns>
    public IReadOnlyList<MediaRequest> OpenRequests()
    {
        // Inside an Atomic that has changed requests, they're read as they stand in it and not kept: it may yet roll back.
        if (Monitor.IsEntered(_lock) && _changedRequests)
        {
            return List(statuses: RequestService.OpenStatuses);
        }

        if (Volatile.Read(ref _open) is { } kept && kept.Writes == Interlocked.Read(ref _writes))
        {
            return kept.Open;
        }

        // Ready first, so reading them never waits on the write lock while holding the open lock.
        EnsureReady();
        lock (_openLock)
        {
            // The count is read before the rows, and a write is counted once committed, so rows read after it are at
            // least that new; one counted since makes them out of date at the next call.
            var writes = Interlocked.Read(ref _writes);
            if (Volatile.Read(ref _open) is { } again && again.Writes == writes)
            {
                return again.Open;
            }

            var open = List(statuses: RequestService.OpenStatuses);
            Volatile.Write(ref _open, new OpenSnapshot(writes, open));
            return open;
        }
    }

    /// <summary>
    /// Returns the requests the library fulfilled with any of these items, for checking them when the items leave the
    /// library. Those an administrator marked available are left out, as they stay so.
    /// </summary>
    /// <param name="libraryItemIds">The library items.</param>
    /// <returns>The requests, newest first within each batch of items.</returns>
    public List<MediaRequest> FulfilledBy(IReadOnlyCollection<Guid> libraryItemIds)
    {
        ArgumentNullException.ThrowIfNull(libraryItemIds);
        var found = new List<MediaRequest>();

        // In batches, well within SQLite's limit on parameters.
        foreach (var batch in libraryItemIds.Distinct().Chunk(500))
        {
            found.AddRange(Select(
                "WHERE LibraryItemId IN (" + string.Join(", ", batch.Select((_, i) => "@item" + i.ToString(CultureInfo.InvariantCulture))) + ")"
                    + " AND Status IN (" + Number(RequestStatus.Available) + ", " + Number(RequestStatus.PartiallyAvailable) + ") AND MarkedAvailable = 0 ORDER BY CreatedUtc DESC",
                p =>
                {
                    for (var i = 0; i < batch.Length; i++)
                    {
                        p.AddWithValue("@item" + i.ToString(CultureInfo.InvariantCulture), Key(batch[i]));
                    }
                }));
        }

        return found;
    }

    /// <summary>Gets the values a request is stored as, column by column, for comparing it before and after a change.</summary>
    /// <param name="r">The request.</param>
    /// <returns>The values, in column order.</returns>
    internal static List<object> StoredValues(MediaRequest r) => Fields(r).Select(f => f.Value).ToList();

    private sealed record OpenSnapshot(long Writes, IReadOnlyList<MediaRequest> Open);

    private void Write(MediaRequest r, string sql)
    {
        ArgumentNullException.ThrowIfNull(r);
        Execute(
            sql,
            p =>
            {
                foreach (var (name, value) in Fields(r))
                {
                    p.AddWithValue(name, value);
                }
            },
            requests: true);
    }

    // Every column's parameter and the value it's stored as, in column order.
    private static List<(string Name, object Value)> Fields(MediaRequest r)
    {
        return new List<(string Name, object Value)>
        {
            ("@Id", Key(r.Id)),
            ("@Type", (int)r.Type),
            ("@Title", r.Title),
            ("@Year", (object?)r.Year ?? DBNull.Value),
            ("@Overview", (object?)r.Overview ?? DBNull.Value),
            ("@PosterUrl", (object?)r.PosterUrl ?? DBNull.Value),
            ("@ProviderIds", JsonSerializer.Serialize(r.ProviderIds)),
            ("@Seasons", r.Seasons is null ? DBNull.Value : JsonSerializer.Serialize(r.Seasons)),
            ("@UserId", Key(r.UserId)),
            ("@UserName", r.UserName),
            ("@RequesterNote", (object?)r.RequesterNote ?? DBNull.Value),
            ("@CreatedUtc", UtcTime.Format(r.CreatedUtc)),
            ("@UpdatedUtc", UtcTime.Format(r.UpdatedUtc)),
            ("@Status", (int)r.Status),
            ("@Note", (object?)r.Note ?? DBNull.Value),
            ("@DecidedBy", (object?)r.DecidedBy ?? DBNull.Value),
            ("@LibraryItemId", r.LibraryItemId is { } item ? Key(item) : DBNull.Value),
            ("@AvailableSeasons", r.AvailableSeasons is null ? DBNull.Value : JsonSerializer.Serialize(r.AvailableSeasons)),
            ("@ArrId", (object?)r.ArrId ?? DBNull.Value),
            ("@Progress", (object?)r.Progress ?? DBNull.Value),
            ("@ProgressText", (object?)r.ProgressText ?? DBNull.Value),
            ("@SendFailures", r.SendFailures),
            ("@RetryAfterUtc", r.RetryAfterUtc is { } retry ? UtcTime.Format(retry) : DBNull.Value),
            ("@BackdropUrl", (object?)r.BackdropUrl ?? DBNull.Value),
            ("@ExpectedEpisodes", r.ExpectedEpisodes is null ? DBNull.Value : JsonSerializer.Serialize(r.ExpectedEpisodes)),
            ("@ServerId", (object?)r.ServerId ?? DBNull.Value),
            ("@ProfileId", (object?)r.ProfileId ?? DBNull.Value),
            ("@QuotaCost", (object?)r.QuotaCost ?? DBNull.Value),
            ("@RemovedFromArr", r.RemovedFromArr ? 1 : 0),
            ("@AgingNotifiedUtc", r.AgingNotifiedUtc is { } aging ? UtcTime.Format(aging) : DBNull.Value),
            ("@ExpiringNotifiedDays", (object?)r.ExpiringNotifiedDays ?? DBNull.Value),
            ("@DecidedUtc", r.DecidedUtc is { } decided ? UtcTime.Format(decided) : DBNull.Value),
            ("@Joined", r.Joined is { Count: > 0 } joined ? JsonSerializer.Serialize(joined) : DBNull.Value),
            ("@SeasonProgress", r.SeasonProgress is null ? DBNull.Value : JsonSerializer.Serialize(r.SeasonProgress)),
            ("@Genres", r.Genres is null ? DBNull.Value : JsonSerializer.Serialize(r.Genres, GenreJson)),
            ("@MarkedAvailable", r.MarkedAvailable ? 1 : 0),
            ("@DownloadDoneUtc", r.DownloadDoneUtc is { } done ? UtcTime.Format(done) : DBNull.Value),
            ("@Specials", r.Specials is null ? DBNull.Value : JsonSerializer.Serialize(r.Specials, GenreJson))
        };
    }

    private static MediaRequest Read(SqliteDataReader r)
    {
        string? Text(int i) => r.IsDBNull(i) ? null : r.GetString(i);
        List<int>? Ints(int i) => r.IsDBNull(i) ? null : JsonSerializer.Deserialize<List<int>>(r.GetString(i));
        return new MediaRequest
        {
            Id = Guid.Parse(r.GetString(0)),
            Type = (RequestType)r.GetInt32(1),
            Title = r.GetString(2),
            Year = r.IsDBNull(3) ? null : r.GetInt32(3),
            Overview = Text(4),
            PosterUrl = Text(5),
            ProviderIds = new Dictionary<string, string>(JsonSerializer.Deserialize<Dictionary<string, string>>(r.GetString(6)) ?? new(), StringComparer.OrdinalIgnoreCase),
            Seasons = Ints(7),
            UserId = Guid.Parse(r.GetString(8)),
            UserName = r.GetString(9),
            RequesterNote = Text(10),
            CreatedUtc = UtcTime.Parse(r.GetString(11)),
            UpdatedUtc = UtcTime.Parse(r.GetString(12)),
            Status = (RequestStatus)r.GetInt32(13),
            Note = Text(14),
            DecidedBy = Text(15),
            LibraryItemId = r.IsDBNull(16) ? null : Guid.Parse(r.GetString(16)),
            AvailableSeasons = Ints(17),
            ArrId = r.IsDBNull(18) ? null : r.GetInt32(18),
            Progress = r.IsDBNull(19) ? null : r.GetDouble(19),
            ProgressText = Text(20),
            SendFailures = r.GetInt32(21),
            RetryAfterUtc = r.IsDBNull(22) ? null : UtcTime.Parse(r.GetString(22)),
            BackdropUrl = Text(23),
            ExpectedEpisodes = r.IsDBNull(24) ? null : JsonSerializer.Deserialize<Dictionary<int, int>>(r.GetString(24)),
            ServerId = Text(25),
            ProfileId = Text(26),
            QuotaCost = r.IsDBNull(27) ? null : r.GetInt32(27),
            RemovedFromArr = r.GetInt32(28) != 0,
            AgingNotifiedUtc = r.IsDBNull(29) ? null : UtcTime.Parse(r.GetString(29)),
            ExpiringNotifiedDays = r.IsDBNull(30) ? null : r.GetInt32(30),
            DecidedUtc = r.IsDBNull(31) ? null : UtcTime.Parse(r.GetString(31)),
            Joined = r.IsDBNull(32) ? null : JsonSerializer.Deserialize<List<RequestJoiner>>(r.GetString(32)),
            SeasonProgress = r.IsDBNull(33) ? null : JsonSerializer.Deserialize<List<SeasonProgress>>(r.GetString(33)),
            Genres = r.IsDBNull(34) ? null : JsonSerializer.Deserialize<List<string>>(r.GetString(34)),
            MarkedAvailable = r.GetInt32(35) != 0,
            DownloadDoneUtc = r.IsDBNull(36) ? null : UtcTime.Parse(r.GetString(36)),
            Specials = r.IsDBNull(37) ? null : JsonSerializer.Deserialize<List<SpecialEpisode>>(r.GetString(37))
        };
    }
}
