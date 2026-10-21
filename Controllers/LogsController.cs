using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using LogLens.Data;
using LogLens.Models;
using LogLens.Services;

namespace LogLens.Controllers;

[AutoValidateAntiforgeryToken]
public class LogsController : Controller
{
    private readonly AppDbContext _db;
    private readonly LogParserService _parser;
    private readonly ILogger<LogsController> _logger;

    private static readonly string[] AllowedExtensions = [".log", ".txt", ".json"];

    public LogsController(AppDbContext db, LogParserService parser, ILogger<LogsController> logger)
    {
        _db = db;
        _parser = parser;
        _logger = logger;
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

        if (model.LogFile != null)
        {
            if (model.LogFile.Length == 0)
            {
                ModelState.AddModelError("LogFile", "The uploaded file is empty.");
                return View(model);
            }
            var ext = Path.GetExtension(model.LogFile.FileName).ToLowerInvariant();
            if (!AllowedExtensions.Contains(ext))
            {
                ModelState.AddModelError("LogFile", "Only .log, .txt, and .json files are allowed.");
                return View(model);
            }

            stream = model.LogFile.OpenReadStream();
            disposeStream = true;
        }
        else if (!string.IsNullOrWhiteSpace(model.PastedLogs))
        {
            stream = new MemoryStream(Encoding.UTF8.GetBytes(model.PastedLogs));
            disposeStream = true;
        }
        else
        {
            ModelState.AddModelError("", "Please upload a file or paste log content.");
            return View(model);
        }

        var sessionName = !string.IsNullOrWhiteSpace(model.SessionName) ? model.SessionName.Trim()
            : model.LogFile != null ? Path.GetFileNameWithoutExtension(model.LogFile.FileName)
            : $"Session {DateTime.Now:yyyy-MM-dd HH:mm}";
        if (sessionName.Length > 200) sessionName = sessionName[..200];

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
            _logger.LogInformation("Imported log session {SessionId} ({SessionName})", session.Id, sessionName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse log session {SessionId}", session.Id);
            _db.LogSessions.Remove(session);
            await _db.SaveChangesAsync();
            ModelState.AddModelError("", "Failed to parse the log file. Please check the format and try again.");
            return View(model);
        }
        finally
        {
            if (disposeStream) stream.Dispose();
        }

        return RedirectToAction("View", new { id = session.Id });
    }

    private async Task StreamInsertEntries(Stream stream, int sessionId)
    {
        await using var conn = new SqliteConnection(_db.Database.GetConnectionString());
        await conn.OpenAsync();

        using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA cache_size=-64000; PRAGMA temp_store=MEMORY;";
            await pragma.ExecuteNonQueryAsync();
        }

        await using var transaction = await conn.BeginTransactionAsync();

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
            await insert.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    [HttpGet]
    public async Task<IActionResult> View(int id, string? search, string? level,
        string? correlationId, string? from, string? to,
        string sortBy = "Timestamp", string sortOrder = "desc", bool useRegex = false)
    {
        var session = await _db.LogSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (session == null) return NotFound();

        await using var conn = new SqliteConnection(_db.Database.GetConnectionString());
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
        ApplyFilters(search, useRegex, level, correlationId, from, to, ref where, sqlParams);

        int filteredCount;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"SELECT COUNT(*) FROM LogEntries {where}";
            foreach (var p in sqlParams) cmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));
            filteredCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

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
                    LastSeen = DateTime.TryParse(rdr.GetString(3), out var dt) ? dt : DateTime.MinValue
                });
            }
        }

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
            Timeline = timeline
        };

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Trace(int id, int entryId)
    {
        await using var conn = new SqliteConnection(_db.Database.GetConnectionString());
        await conn.OpenAsync();

        string? correlationId = null;

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT CorrelationId, Timestamp FROM LogEntries WHERE Id = $id AND SessionId = $sid";
            cmd.Parameters.AddWithValue("$id", entryId);
            cmd.Parameters.AddWithValue("$sid", id);
            using var rdr = await cmd.ExecuteReaderAsync();
            if (await rdr.ReadAsync())
            {
                correlationId = rdr.IsDBNull(0) ? null : rdr.GetString(0);
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
            _logger.LogInformation("Deleted log session {SessionId}", id);
        }
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public async Task<IActionResult> EntriesApi(int id, string? search, string? level,
        string? correlationId, string? from, string? to,
        string sortBy = "Timestamp", string sortOrder = "desc", int offset = 0, int limit = 100, bool useRegex = false)
    {
        limit = Math.Clamp(limit, 1, 500);

        await using var conn = new SqliteConnection(_db.Database.GetConnectionString());
        await conn.OpenAsync();
        if (useRegex) RegisterRegexp(conn);

        var where = "WHERE SessionId = $sid";
        var sqlParams = new List<SqliteParameter> { new("$sid", id) };
        ApplyFilters(search, useRegex, level, correlationId, from, to, ref where, sqlParams);

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

        return Json(new { totalCount, entries });
    }

    [HttpGet]
    public async Task<IActionResult> Export(int id, string format = "csv", string? search = null, string? level = null)
    {
        var query = _db.LogEntries.AsNoTracking().Where(e => e.SessionId == id);
        if (!string.IsNullOrWhiteSpace(level))
            query = query.Where(e => e.Level == level);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(e => e.Message.Contains(search) || e.Source.Contains(search));
        query = query.OrderBy(e => e.Timestamp);

        var fileName = $"logs-{id}.{format}";
        Response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"";

        if (format == "json")
        {
            Response.ContentType = "application/json";
            await Response.StartAsync();

            await Response.WriteAsync("[");
            bool first = true;
            await foreach (var e in query.AsAsyncEnumerable())
            {
                if (!first) await Response.WriteAsync(",");
                first = false;
                var json = JsonSerializer.Serialize(new
                {
                    e.Timestamp,
                    e.Level,
                    e.Source,
                    e.Message,
                    e.StackTrace
                });
                await Response.WriteAsync(json);
            }
            await Response.WriteAsync("]");
            return new EmptyResult();
        }

        Response.ContentType = "text/csv";
        await Response.StartAsync();
        await Response.WriteAsync("Timestamp,Level,Source,Message\n");
        await foreach (var e in query.AsAsyncEnumerable())
        {
            await Response.WriteAsync($"\"{e.Timestamp:yyyy-MM-dd HH:mm:ss}\",\"{e.Level}\",\"{Escape(e.Source)}\",\"{Escape(e.Message)}\"\n");
        }
        return new EmptyResult();
    }

    private static void RegisterRegexp(SqliteConnection conn)
    {
        conn.CreateFunction("regexp", (string pattern, string input) =>
        {
            try { return Regex.IsMatch(input ?? "", pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)); }
            catch { return false; }
        });
    }

    private static void ApplyFilters(string? search, bool useRegex, string? level,
        string? correlationId, string? from, string? to,
        ref string where, List<SqliteParameter> sqlParams)
    {
        if (!string.IsNullOrWhiteSpace(search))
        {
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
    }

    private static string Escape(string s) =>
        s.Replace("\"", "\"\"").Replace("\r", "").Replace("\n", " ");
}
