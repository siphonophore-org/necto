using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Siphonophore.Stolon;
using Microsoft.Data.Sqlite;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

#pragma warning disable CA2100 // SQL commands use only internal constants and parameterized values

namespace Jellyfin.Plugin.Necto.Services.Requests;

/// <summary>
/// The issues users report about library items, kept in the same database as the requests.
/// </summary>
public sealed partial class RequestDatabase
{
    // Column order must match ReadIssue.
    private const string IssueColumns = "Id, ItemId, ItemType, Title, SeriesName, SeriesId, SeasonNumber, EpisodeNumber, Year, Path, FileSize, FileModifiedUtc, "
        + "ProviderIds, SeriesProviderIds, State, Reporters, Notes, CreatedUtc, UpdatedUtc, FileChanged, DecidedBy";

    // An issue with its item's snapshot, its reporters and its notes, each list as JSON. Lists by state come newest
    // first, and an item's issues are found by its id.
    private const string IssuesSchema = @"
        CREATE TABLE IF NOT EXISTS Issues (
            Id TEXT PRIMARY KEY,
            ItemId TEXT NOT NULL,
            ItemType TEXT NOT NULL,
            Title TEXT NOT NULL COLLATE NOCASE,
            SeriesName TEXT COLLATE NOCASE,
            SeriesId TEXT,
            SeasonNumber INTEGER,
            EpisodeNumber INTEGER,
            Year INTEGER,
            Path TEXT,
            FileSize INTEGER,
            FileModifiedUtc TEXT,
            ProviderIds TEXT NOT NULL,
            SeriesProviderIds TEXT,
            State INTEGER NOT NULL,
            Reporters TEXT NOT NULL,
            Notes TEXT NOT NULL,
            CreatedUtc TEXT NOT NULL,
            UpdatedUtc TEXT NOT NULL,
            FileChanged INTEGER NOT NULL DEFAULT 0,
            DecidedBy TEXT);
        CREATE INDEX IF NOT EXISTS idx_issues_item ON Issues(ItemId);
        CREATE INDEX IF NOT EXISTS idx_issues_state ON Issues(State, UpdatedUtc);";

    /// <summary>Returns an issue by id, or null.</summary>
    /// <param name="id">The issue id.</param>
    /// <returns>The issue, or null.</returns>
    public Issue? GetIssue(Guid id)
        => SelectIssues("WHERE Id = @id", p => p.AddWithValue("@id", Key(id))).FirstOrDefault();

    /// <summary>Adds an issue.</summary>
    /// <param name="issue">The issue.</param>
    public void InsertIssue(Issue issue)
        => WriteIssue(issue, "INSERT INTO Issues (" + IssueColumns + ") VALUES (" + string.Join(", ", IssueColumnNames().Select(c => "@" + c)) + ")");

    /// <summary>Writes every field of an existing issue.</summary>
    /// <param name="issue">The issue.</param>
    public void UpdateIssue(Issue issue)
        => WriteIssue(issue, "UPDATE Issues SET " + string.Join(", ", IssueColumnNames().Skip(1).Select(c => c + " = @" + c)) + " WHERE Id = @Id");

    /// <summary>Returns every issue about an item, newest first.</summary>
    /// <param name="itemId">The library item.</param>
    /// <returns>The issues.</returns>
    public List<Issue> IssuesForItem(Guid itemId)
        => SelectIssues("WHERE ItemId = @item ORDER BY CreatedUtc DESC", p => p.AddWithValue("@item", Key(itemId)));

    /// <summary>Returns every open issue, Reported or Confirmed, newest first.</summary>
    /// <returns>The issues.</returns>
    public List<Issue> OpenIssues()
        => SelectIssues("WHERE State IN (" + IssueNumber(IssueState.Reported) + ", " + IssueNumber(IssueState.Confirmed) + ") ORDER BY CreatedUtc DESC", null);

    /// <summary>
    /// Returns a page of issues, most recently changed first, narrowed by state, by what a reporter says is wrong, by
    /// reporter, and by a search of the title, the show and the reporters' names.
    /// </summary>
    /// <param name="query">The page, and the search in <see cref="PagedQuery.Search"/>.</param>
    /// <param name="states">Only these states, when set.</param>
    /// <param name="type">Only issues where some reporter says this is wrong, when set.</param>
    /// <param name="reporter">Only issues this user reported or joined, when set.</param>
    /// <returns>The page.</returns>
    public PagedResult<Issue> IssuePage(PagedQuery query, IReadOnlyCollection<IssueState>? states = null, IssueType? type = null, Guid? reporter = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        var (skip, take) = Window(query);
        var where = new List<string>();
        if (states is { Count: > 0 })
        {
            where.Add("State IN (" + string.Join(", ", states.Select(IssueNumber)) + ")");
        }

        if (type is not null)
        {
            where.Add("EXISTS (SELECT 1 FROM json_each(Reporters) WHERE json_extract(value, '$.Type') = @type)");
        }

        if (reporter is not null)
        {
            where.Add("EXISTS (SELECT 1 FROM json_each(Reporters) WHERE REPLACE(LOWER(json_extract(value, '$.UserId')), '-', '') = @reporter)");
        }

        var search = (query.Search ?? string.Empty).Trim();
        if (search.Length > 0)
        {
            where.Add("(Title LIKE @search ESCAPE '\\' OR SeriesName LIKE @search ESCAPE '\\' OR Reporters LIKE @search ESCAPE '\\')");
        }

        var clause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : string.Empty;
        void Bind(SqliteParameterCollection p)
        {
            if (type is { } t)
            {
                p.AddWithValue("@type", (int)t);
            }

            if (reporter is { } who)
            {
                p.AddWithValue("@reporter", who.ToString("N", CultureInfo.InvariantCulture));
            }

            if (search.Length > 0)
            {
                p.AddWithValue("@search", "%" + search.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%");
            }
        }

        return Query(
            (connection, transaction) =>
            {
                var total = Convert.ToInt32(ScalarOn(connection, transaction, "SELECT COUNT(*) FROM Issues " + clause, Bind), CultureInfo.InvariantCulture);
                var items = SelectIssuesOn(connection, transaction, clause + " ORDER BY UpdatedUtc DESC LIMIT " + take.ToString(CultureInfo.InvariantCulture) + " OFFSET " + skip.ToString(CultureInfo.InvariantCulture), Bind);
                return new PagedResult<Issue>(items, total, skip, take);
            },
            together: true);
    }

    /// <summary>Counts the issues in each state.</summary>
    /// <returns>The counts, by state.</returns>
    public Dictionary<IssueState, int> CountIssuesByState() => Query((connection, transaction) =>
    {
        var counts = new Dictionary<IssueState, int>();
        using var cmd = Command(connection, transaction, "SELECT State, COUNT(*) FROM Issues GROUP BY State");
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            counts[(IssueState)reader.GetInt32(0)] = reader.GetInt32(1);
        }

        return counts;
    });

    /// <summary>Lists everyone who reported or joined an issue, each with how many issues and the name on their latest report.</summary>
    /// <returns>The reporters, by name.</returns>
    public List<Requester> IssueReporters()
    {
        var latest = new Dictionary<Guid, (string Name, DateTime When, int Count)>();
        foreach (var reporter in SelectIssues(string.Empty, null).SelectMany(i => i.Reporters.DistinctBy(r => r.UserId)))
        {
            var seen = latest.TryGetValue(reporter.UserId, out var was);
            var newer = !seen || reporter.ReportedUtc >= was.When;
            latest[reporter.UserId] = (newer ? reporter.UserName : was.Name, newer ? reporter.ReportedUtc : was.When, (seen ? was.Count : 0) + 1);
        }

        return latest.Select(p => new Requester(p.Key, p.Value.Name, p.Value.Count)).OrderBy(r => r.UserName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Counts how many issues someone reported or joined since a time, for their limit.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="sinceUtc">The start of the window.</param>
    /// <returns>How many.</returns>
    public int CountReportsSince(Guid userId, DateTime sinceUtc)
        => SelectIssues("WHERE Reporters LIKE @user", p => p.AddWithValue("@user", "%" + userId.ToString("D", CultureInfo.InvariantCulture) + "%"))
            .Count(i => i.Reporters.Any(r => r.UserId == userId && r.ReportedUtc >= sinceUtc));

    /// <summary>Removes resolved, declined and rescinded issues last changed before a time.</summary>
    /// <param name="beforeUtc">The cut-off.</param>
    /// <returns>How many were removed.</returns>
    public int DeleteClosedIssuesBefore(DateTime beforeUtc)
        => Execute(
            "DELETE FROM Issues WHERE State IN (" + IssueNumber(IssueState.Resolved) + ", " + IssueNumber(IssueState.Declined) + ", " + IssueNumber(IssueState.Rescinded) + ") AND UpdatedUtc < @before",
            p => p.AddWithValue("@before", UtcTime.Format(beforeUtc)));

    private static string IssueNumber(IssueState state) => ((int)state).ToString(CultureInfo.InvariantCulture);

    private static string[] IssueColumnNames() => IssueColumns.Split(',', StringSplitOptions.TrimEntries);

    private List<Issue> SelectIssues(string clause, Action<SqliteParameterCollection>? bind)
        => Query((connection, transaction) => SelectIssuesOn(connection, transaction, clause, bind));

    private static List<Issue> SelectIssuesOn(SqliteConnection connection, SqliteTransaction? transaction, string clause, Action<SqliteParameterCollection>? bind)
    {
        using var cmd = Command(connection, transaction, "SELECT " + IssueColumns + " FROM Issues " + clause);
        bind?.Invoke(cmd.Parameters);
        using var reader = cmd.ExecuteReader();
        var list = new List<Issue>();
        while (reader.Read())
        {
            list.Add(ReadIssue(reader));
        }

        return list;
    }

    private void WriteIssue(Issue issue, string sql)
    {
        ArgumentNullException.ThrowIfNull(issue);
        Execute(
            sql,
            p =>
            {
                p.AddWithValue("@Id", Key(issue.Id));
                p.AddWithValue("@ItemId", Key(issue.ItemId));
                p.AddWithValue("@ItemType", issue.ItemType);
                p.AddWithValue("@Title", issue.Title);
                p.AddWithValue("@SeriesName", (object?)issue.SeriesName ?? DBNull.Value);
                p.AddWithValue("@SeriesId", issue.SeriesId is { } series ? Key(series) : DBNull.Value);
                p.AddWithValue("@SeasonNumber", (object?)issue.SeasonNumber ?? DBNull.Value);
                p.AddWithValue("@EpisodeNumber", (object?)issue.EpisodeNumber ?? DBNull.Value);
                p.AddWithValue("@Year", (object?)issue.Year ?? DBNull.Value);
                p.AddWithValue("@Path", (object?)issue.Path ?? DBNull.Value);
                p.AddWithValue("@FileSize", (object?)issue.FileSize ?? DBNull.Value);
                p.AddWithValue("@FileModifiedUtc", issue.FileModifiedUtc is { } modified ? UtcTime.Format(modified) : DBNull.Value);
                p.AddWithValue("@ProviderIds", JsonSerializer.Serialize(issue.ProviderIds ?? new Dictionary<string, string>()));
                p.AddWithValue("@SeriesProviderIds", issue.SeriesProviderIds is null ? DBNull.Value : JsonSerializer.Serialize(issue.SeriesProviderIds));
                p.AddWithValue("@State", (int)issue.State);
                p.AddWithValue("@Reporters", JsonSerializer.Serialize(issue.Reporters ?? new List<IssueReporter>(), GenreJson));
                p.AddWithValue("@Notes", JsonSerializer.Serialize(issue.Notes ?? new List<IssueNote>(), GenreJson));
                p.AddWithValue("@CreatedUtc", UtcTime.Format(issue.CreatedUtc));
                p.AddWithValue("@UpdatedUtc", UtcTime.Format(issue.UpdatedUtc));
                p.AddWithValue("@FileChanged", issue.FileChanged ? 1 : 0);
                p.AddWithValue("@DecidedBy", (object?)issue.DecidedBy ?? DBNull.Value);
            });
    }

    private static Issue ReadIssue(SqliteDataReader r)
    {
        string? Text(int i) => r.IsDBNull(i) ? null : r.GetString(i);
        int? Int(int i) => r.IsDBNull(i) ? null : r.GetInt32(i);
        Dictionary<string, string> Ids(string json) => new(JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new(), StringComparer.OrdinalIgnoreCase);
        return new Issue
        {
            Id = Guid.Parse(r.GetString(0)),
            ItemId = Guid.Parse(r.GetString(1)),
            ItemType = r.GetString(2),
            Title = r.GetString(3),
            SeriesName = Text(4),
            SeriesId = r.IsDBNull(5) ? null : Guid.Parse(r.GetString(5)),
            SeasonNumber = Int(6),
            EpisodeNumber = Int(7),
            Year = Int(8),
            Path = Text(9),
            FileSize = r.IsDBNull(10) ? null : r.GetInt64(10),
            FileModifiedUtc = r.IsDBNull(11) ? null : UtcTime.Parse(r.GetString(11)),
            ProviderIds = Ids(r.GetString(12)),
            SeriesProviderIds = r.IsDBNull(13) ? null : Ids(r.GetString(13)),
            State = (IssueState)r.GetInt32(14),
            Reporters = JsonSerializer.Deserialize<List<IssueReporter>>(r.GetString(15)) ?? new(),
            Notes = JsonSerializer.Deserialize<List<IssueNote>>(r.GetString(16)) ?? new(),
            CreatedUtc = UtcTime.Parse(r.GetString(17)),
            UpdatedUtc = UtcTime.Parse(r.GetString(18)),
            FileChanged = r.GetInt32(19) != 0,
            DecidedBy = Text(20)
        };
    }
}
