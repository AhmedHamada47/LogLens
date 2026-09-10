using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using LogLens.Data;
using LogLens.Models;
using LogLens.Services;

namespace LogLens.Controllers;

public class LogsController : Controller
{
    private readonly AppDbContext _db;
    private readonly LogParserService _parser;
    private const int PageSize = 200;

    public LogsController(AppDbContext db, LogParserService parser)
    {
        _db = db;
        _parser = parser;
    }

    [HttpGet]
    public IActionResult Upload()
    {
        return View(new LogUploadViewModel());
    }

    [HttpPost]
    [RequestSizeLimit(500L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 500L * 1024 * 1024, ValueLengthLimit = 50 * 1024 * 1024)]
    public async Task<IActionResult> Upload([FromForm] LogUploadViewModel model)
    {
        if (model == null)
        {
            ModelState.AddModelError("", "No data was received.");
            return View(new LogUploadViewModel());
        }

        Stream? stream = null;
        bool disposeStream = false;

        if (model.LogFile != null && model.LogFile.Length > 0)
        {
            stream = model.LogFile.OpenReadStream();
            disposeStream = true;
        }
        else if (!string.IsNullOrWhiteSpace(model.PastedLogs))
        {
            stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(model.PastedLogs));
            disposeStream = true;
        }
        else
        {
            ModelState.AddModelError("", "Please upload a file or paste log content.");
            return View(model);
        }

        var sessionName = string.IsNullOrWhiteSpace(model.SessionName)
            ? $"Session {DateTime.Now:yyyy-MM-dd HH:mm}"
            : model.SessionName;

        var session = new LogSession
        {
            Name = sessionName,
            SourceFileName = model.LogFile?.FileName,
            CreatedAt = DateTime.UtcNow
        };

        _db.LogSessions.Add(session);
        await _db.SaveChangesAsync();

        try
        {
            await StreamInsertEntries(stream, session.Id);
        }
        finally
        {
            if (disposeStream) stream.Dispose();
        }

        return RedirectToAction("View", new { id = session.Id });
    }

    private async Task StreamInsertEntries(Stream stream, int sessionId)
    {
        var conn = new SqliteConnection(_db.Database.GetConnectionString());
        await conn.OpenAsync();

        try
        {
            using (var pragma = conn.CreateCommand())
            {
                pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=OFF; PRAGMA cache_size=-64000; PRAGMA temp_store=MEMORY;";
                await pragma.ExecuteNonQueryAsync();
            }

            using var transaction = await conn.BeginTransactionAsync();

            using var insert = conn.CreateCommand();
            insert.CommandText = @"INSERT INTO LogEntries (Timestamp, Level, Source, Message, StackTrace, CorrelationId, RawLine, SessionId)
                                   VALUES ($ts, $lvl, $src, $msg, $st, $cid, $raw, $sid)";

            var pTs = new SqliteParameter("$ts", "");
            var pLvl = new SqliteParameter("$lvl", "");
            var pSrc = new SqliteParameter("$src", "");
            var pMsg = new SqliteParameter("$msg", "");
            var pSt = new SqliteParameter("$st", DBNull.Value);
            var pCid = new SqliteParameter("$cid", DBNull.Value);
            var pRaw = new SqliteParameter("$raw", DBNull.Value);
            var pSid = new SqliteParameter("$sid", sessionId);
            insert.Parameters.AddRange(new[] { pTs, pLvl, pSrc, pMsg, pSt, pCid, pRaw, pSid });
            await insert.PrepareAsync();

            foreach (var entry in _parser.ParseStreamEnumerable(stream))
            {
                pTs.Value = entry.Timestamp.ToString("o");
                pLvl.Value = entry.Level;
                pSrc.Value = entry.Source ?? "";
                pMsg.Value = entry.Message;
                pSt.Value = (object?)entry.StackTrace ?? DBNull.Value;
                pCid.Value = (object?)entry.CorrelationId ?? DBNull.Value;
                pRaw.Value = (object?)entry.RawLine ?? DBNull.Value;
                insert.ExecuteNonQuery();
            }

            await transaction.CommitAsync();

            using (var restore = conn.CreateCommand())
            {
                restore.CommandText = "PRAGMA synchronous=NORMAL;";
                await restore.ExecuteNonQueryAsync();
            }
        }
        finally
        {
            await conn.CloseAsync();
            conn.Dispose();
        }
    }

    [HttpGet]
    public async Task<IActionResult> View(int id, string? search, string? level,
        string? correlationId, string? from, string? to,
        string sortBy = "Timestamp", string sortOrder = "desc", bool useRegex = false)
    {
        var session = await _db.LogSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (session == null) return NotFound();

        var conn = new SqliteConnection(_db.Database.GetConnectionString());
        await conn.OpenAsync();
        if (useRegex) RegisterRegexp(conn);

        var levelCounts = new Dictionary<string, int>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT Level, COUNT(*) FROM LogEntries WHERE SessionId = $sid GROUP BY Level";
            cmd.Parameters.AddWithValue("$sid", id);
            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
                levelCounts[rdr.GetString(0)] = rdr.GetInt32(1);
        }
        var totalCount = levelCounts.Values.Sum();

        var where = "WHERE SessionId = $sid";
        var sqlParams = new List<SqliteParameter> { new("$sid", id) };

        ApplySearchFilter(search, useRegex, ref where, sqlParams);
        if (!string.IsNullOrWhiteSpace(level))
        {
            where += " AND Level = $level";
            sqlParams.Add(new("$level", level));
        }
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            where += " AND CorrelationId = $cid";
            sqlParams.Add(new("$cid", correlationId));
        }
        if (!string.IsNullOrWhiteSpace(from))
        {
            where += " AND SUBSTR(Timestamp, 12, 5) >= $from";
            sqlParams.Add(new("$from", from));
        }
        if (!string.IsNullOrWhiteSpace(to))
        {
            where += " AND SUBSTR(Timestamp, 12, 5) <= $to";
            sqlParams.Add(new("$to", to));
        }

        int filteredCount;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"SELECT COUNT(*) FROM LogEntries {where}";
            foreach (var p in sqlParams) cmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));
            filteredCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        // Error summary - top unique error/fatal messages
        var errorSummary = new List<ErrorSummaryItem>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT SUBSTR(Message, 1, 150), Level, COUNT(*), MAX(Timestamp)
                FROM LogEntries WHERE SessionId = $sid AND Level IN ('ERROR','FATAL')
                GROUP BY SUBSTR(Message, 1, 150), Level ORDER BY COUNT(*) DESC LIMIT 10";
            cmd.Parameters.AddWithValue("$sid", id);
            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                errorSummary.Add(new ErrorSummaryItem
                {
                    Message = rdr.GetString(0),
                    Level = rdr.GetString(1),
                    Count = rdr.GetInt32(2),
                    LastSeen = DateTime.Parse(rdr.GetString(3))
                });
            }
        }

        // Timeline - bucket by minute
        var timeline = new List<TimelineBucket>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT SUBSTR(Timestamp, 1, 16) AS bucket,
                SUM(CASE WHEN Level='INFO' THEN 1 ELSE 0 END),
                SUM(CASE WHEN Level='WARN' THEN 1 ELSE 0 END),
                SUM(CASE WHEN Level IN ('ERROR','FATAL') THEN 1 ELSE 0 END)
                FROM LogEntries WHERE SessionId = $sid
                GROUP BY bucket ORDER BY bucket";
            cmd.Parameters.AddWithValue("$sid", id);
            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                timeline.Add(new TimelineBucket
                {
                    Time = rdr.GetString(0),
                    Info = rdr.GetInt32(1),
                    Warn = rdr.GetInt32(2),
                    Error = rdr.GetInt32(3)
                });
            }
        }

        // Field statistics - top sources
        var topSources = new List<FieldStat>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT Source, COUNT(*) as cnt FROM LogEntries
                WHERE SessionId = $sid AND Source != '' GROUP BY Source ORDER BY cnt DESC LIMIT 15";
            cmd.Parameters.AddWithValue("$sid", id);
            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
                topSources.Add(new FieldStat { Value = rdr.GetString(0), Count = rdr.GetInt32(1) });
        }

        // Top correlation IDs
        var topCorrelations = new List<FieldStat>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT CorrelationId, COUNT(*) as cnt FROM LogEntries
                WHERE SessionId = $sid AND CorrelationId IS NOT NULL AND CorrelationId != ''
                GROUP BY CorrelationId ORDER BY cnt DESC LIMIT 10";
            cmd.Parameters.AddWithValue("$sid", id);
            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
                topCorrelations.Add(new FieldStat { Value = rdr.GetString(0), Count = rdr.GetInt32(1) });
        }

        await conn.CloseAsync();
        conn.Dispose();

        var vm = new LogFilterViewModel
        {
            SessionId = id,
            Session = session,
            SearchTerm = search,
            Level = level,
            CorrelationId = correlationId,
            FromTime = from,
            ToTime = to,
            SortBy = sortBy,
            SortOrder = sortOrder,
            UseRegex = useRegex,
            LevelCounts = levelCounts,
            TotalCount = totalCount,
            FilteredCount = filteredCount,
            ErrorSummary = errorSummary,
            Timeline = timeline,
            TopSources = topSources,
            TopCorrelations = topCorrelations
        };

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Trace(int id, int entryId)
    {
        var conn = new SqliteConnection(_db.Database.GetConnectionString());
        await conn.OpenAsync();

        string? correlationId = null;
        DateTime errorTimestamp = DateTime.MinValue;

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT CorrelationId, Timestamp FROM LogEntries WHERE Id = $id AND SessionId = $sid";
            cmd.Parameters.AddWithValue("$id", entryId);
            cmd.Parameters.AddWithValue("$sid", id);
            using var rdr = await cmd.ExecuteReaderAsync();
            if (await rdr.ReadAsync())
            {
                correlationId = rdr.IsDBNull(0) ? null : rdr.GetString(0);
                errorTimestamp = DateTime.Parse(rdr.GetString(1));
            }
        }

        var entries = new List<object>();

        if (!string.IsNullOrEmpty(correlationId))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT Id, Timestamp, Level, Source, Message, StackTrace
                FROM LogEntries WHERE SessionId = $sid AND CorrelationId = $cid
                ORDER BY Timestamp ASC LIMIT 100";
            cmd.Parameters.AddWithValue("$sid", id);
            cmd.Parameters.AddWithValue("$cid", correlationId);
            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                entries.Add(new
                {
                    id = rdr.GetInt32(0),
                    timestamp = rdr.GetString(1),
                    level = rdr.GetString(2),
                    source = rdr.GetString(3),
                    message = rdr.GetString(4),
                    stackTrace = rdr.IsDBNull(5) ? null : rdr.GetString(5)
                });
            }
        }
        else
        {
            // No CorrelationId — get 20 entries before and 20 after the target entry by Id
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT Id, Timestamp, Level, Source, Message, StackTrace FROM (
                SELECT Id, Timestamp, Level, Source, Message, StackTrace FROM LogEntries
                WHERE SessionId = $sid AND Id <= $eid ORDER BY Id DESC LIMIT 20
            ) UNION ALL SELECT Id, Timestamp, Level, Source, Message, StackTrace FROM (
                SELECT Id, Timestamp, Level, Source, Message, StackTrace FROM LogEntries
                WHERE SessionId = $sid AND Id > $eid ORDER BY Id ASC LIMIT 20
            ) ORDER BY Id ASC";
            cmd.Parameters.AddWithValue("$eid", entryId);
            cmd.Parameters.AddWithValue("$sid", id);
            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                entries.Add(new
                {
                    id = rdr.GetInt32(0),
                    timestamp = rdr.GetString(1),
                    level = rdr.GetString(2),
                    source = rdr.GetString(3),
                    message = rdr.GetString(4),
                    stackTrace = rdr.IsDBNull(5) ? null : rdr.GetString(5)
                });
            }
        }

        await conn.CloseAsync();
        conn.Dispose();

        return Json(new
        {
            correlationId,
            mode = !string.IsNullOrEmpty(correlationId) ? "correlation" : "nearby",
            entryId,
            entries
        });
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var session = await _db.LogSessions.FindAsync(id);
        if (session != null)
        {
            _db.LogSessions.Remove(session);
            await _db.SaveChangesAsync();
        }
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public async Task<IActionResult> EntriesApi(int id, string? search, string? level,
        string? correlationId, string? from, string? to,
        string sortBy = "Timestamp", string sortOrder = "desc", int offset = 0, int limit = 100, bool useRegex = false)
    {
        limit = Math.Clamp(limit, 1, 500);

        var conn = new SqliteConnection(_db.Database.GetConnectionString());
        await conn.OpenAsync();
        if (useRegex) RegisterRegexp(conn);

        var where = "WHERE SessionId = $sid";
        var sqlParams = new List<SqliteParameter> { new("$sid", id) };

        ApplySearchFilter(search, useRegex, ref where, sqlParams);
        if (!string.IsNullOrWhiteSpace(level))
        {
            where += " AND Level = $level";
            sqlParams.Add(new("$level", level));
        }
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            where += " AND CorrelationId = $cid";
            sqlParams.Add(new("$cid", correlationId));
        }
        if (!string.IsNullOrWhiteSpace(from))
        {
            where += " AND SUBSTR(Timestamp, 12, 5) >= $from";
            sqlParams.Add(new("$from", from));
        }
        if (!string.IsNullOrWhiteSpace(to))
        {
            where += " AND SUBSTR(Timestamp, 12, 5) <= $to";
            sqlParams.Add(new("$to", to));
        }

        int totalCount;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"SELECT COUNT(*) FROM LogEntries {where}";
            foreach (var p in sqlParams) cmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));
            totalCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        var orderCol = sortBy == "Level" ? "Level" : sortBy == "Source" ? "Source" : "Timestamp";
        var orderDir = sortOrder == "asc" ? "ASC" : "DESC";

        var entries = new List<object>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"SELECT Id, Timestamp, Level, Source, Message, StackTrace, CorrelationId FROM LogEntries {where} ORDER BY {orderCol} {orderDir} LIMIT $limit OFFSET $offset";
            foreach (var p in sqlParams) cmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));
            cmd.Parameters.AddWithValue("$limit", limit);
            cmd.Parameters.AddWithValue("$offset", offset);
            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                entries.Add(new
                {
                    id = rdr.GetInt32(0),
                    timestamp = rdr.GetString(1),
                    level = rdr.GetString(2),
                    source = rdr.GetString(3),
                    message = rdr.GetString(4),
                    stackTrace = rdr.IsDBNull(5) ? null : rdr.GetString(5),
                    correlationId = rdr.IsDBNull(6) ? null : rdr.GetString(6)
                });
            }
        }

        await conn.CloseAsync();
        conn.Dispose();

        return Json(new { totalCount, entries });
    }

    [HttpGet]
    public async Task<IActionResult> Export(int id, string format = "csv")
    {
        var entries = await _db.LogEntries
            .AsNoTracking()
            .Where(e => e.SessionId == id)
            .OrderBy(e => e.Timestamp)
            .ToListAsync();

        if (format == "json")
        {
            var json = System.Text.Json.JsonSerializer.Serialize(entries.Select(e => new
            {
                e.Timestamp,
                e.Level,
                e.Source,
                e.Message,
                e.StackTrace
            }), new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

            return File(System.Text.Encoding.UTF8.GetBytes(json), "application/json", $"logs-{id}.json");
        }

        var csv = "Timestamp,Level,Source,Message\n" +
            string.Join("\n", entries.Select(e =>
                $"\"{e.Timestamp:yyyy-MM-dd HH:mm:ss}\",\"{e.Level}\",\"{Escape(e.Source)}\",\"{Escape(e.Message)}\""));

        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", $"logs-{id}.csv");
    }

    private static void RegisterRegexp(SqliteConnection conn)
    {
        conn.CreateFunction("regexp", (string pattern, string input) =>
        {
            try { return Regex.IsMatch(input ?? "", pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)); }
            catch { return false; }
        });
    }

    private static void ApplySearchFilter(string? search, bool useRegex, ref string where, List<SqliteParameter> sqlParams)
    {
        if (string.IsNullOrWhiteSpace(search)) return;

        if (useRegex)
        {
            where += " AND (regexp($search, Message) OR regexp($search, Source))";
            sqlParams.Add(new("$search", search));
        }
        else
        {
            where += " AND (Message LIKE $search OR Source LIKE $search)";
            sqlParams.Add(new("$search", $"%{search}%"));
        }
    }

    private static string Escape(string s) =>
        s.Replace("\"", "\"\"").Replace("\r", "").Replace("\n", " ");
}
