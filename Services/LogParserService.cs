using System.Text.RegularExpressions;
using LogLens.Models;

namespace LogLens.Services;

public class LogParserService
{
    private static readonly Regex SerilogTz = new(
        @"^(?<ts>\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}(?:[\.,]\d+)?)\s+[+-]\d{2}:\d{2}\s+\[(?<level>[A-Za-z]+)\]\s+\[(?<source>[^\]]+)\](?:\s*\[[^\]]*\])*\s*(?<msg>.*)",
        RegexOptions.Compiled);

    private static readonly Regex SerilogNoTz = new(
        @"^(?<ts>\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}(?:[\.,]\d+)?)\s+\[(?<level>[A-Za-z]+)\]\s+(?<source>\S+)?\s*[-:]?\s*(?<msg>.+)",
        RegexOptions.Compiled);

    private static readonly Regex Standard = new(
        @"^(?<ts>\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}(?:[\.,]\d+)?)\s+(?<level>TRACE|DEBUG|INFO|WARN|WARNING|ERROR|FATAL|CRITICAL|ERR|WRN|INF|DBG|FTL|TRC|VRB)\s+(?<source>\S+)?\s*[-:]?\s*(?<msg>.+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Bracketed = new(
        @"^\[?(?<ts>\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}(?:[\.,]\d+)?)\]?\s*\[?(?<level>TRACE|DEBUG|INFO|WARN|WARNING|ERROR|FATAL|CRITICAL|ERR|WRN|INF|DBG|FTL|TRC|VRB)\]?\s*(?<msg>.+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex[] Patterns = [SerilogTz, SerilogNoTz, Standard, Bracketed];

    // Enough for long async/EF Core stack traces with several inner exceptions
    private const int MaxContinuationLines = 500;

    // Header line of an exception or inner exception: "Some.Namespace.FooException (0x80131904): message"
    private static readonly Regex ExceptionHeader = new(
        @"^[\w.`+]*(?:Exception|Error)(?:\s*\(0x[0-9A-Fa-f]+\))?:", RegexOptions.Compiled);

    // The id must start with a real character, so an empty "[CorrelationId:]" yields no id (not ":")
    private static readonly Regex CorrelationIdPattern = new(
        @"\[CorrelationId:?\s*([^\]\s:][^\]]*)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public IEnumerable<LogEntry> ParseStreamEnumerable(Stream stream)
    {
        LogEntry? current = null;
        int continuationCount = 0;
        Regex? detectedPattern = null;
        int detectLines = 0;

        using var reader = new StreamReader(stream, bufferSize: 4 * 1024 * 1024);
        string? rawLine;
        while ((rawLine = reader.ReadLine()) != null)
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line)) continue;

            LogEntry? entry;
            if (detectedPattern != null)
            {
                entry = TryParseWithPattern(line, detectedPattern);
                if (entry == null && detectLines < 50)
                    entry = TryParseLine(line, out _);
            }
            else
            {
                entry = TryParseLine(line, out var matched);
                if (matched != null && ++detectLines >= 3)
                    detectedPattern = matched;
            }

            if (entry != null)
            {
                if (current != null) yield return current;
                current = entry;
                continuationCount = 0;
            }
            else if (current != null && continuationCount < MaxContinuationLines)
            {
                continuationCount++;
                var trimmed = line.TrimStart();
                var isFrameOrMarker = trimmed.StartsWith("at ") || trimmed.StartsWith("---") || trimmed.StartsWith("Exception");
                var isHeader = !isFrameOrMarker && ExceptionHeader.IsMatch(trimmed);

                // Frames, separators and exception headers build the stack trace (headers give the viewer the exception chain)
                if (isFrameOrMarker || isHeader)
                {
                    current.StackTrace = string.IsNullOrEmpty(current.StackTrace)
                        ? line : current.StackTrace + "\n" + line;
                }

                // Everything else, and exception headers too, stays in the message so search and Top Errors keep working
                if (!isFrameOrMarker)
                {
                    var extra = trimmed.Length > 500 ? trimmed[..500] : trimmed;
                    if (current.Message.Length < 2000)
                        current.Message += " " + extra;
                }
            }
        }

        if (current != null) yield return current;
    }

    public List<LogEntry> ParseStream(Stream stream)
    {
        return ParseStreamEnumerable(stream).ToList();
    }

    private static LogEntry? TryParseLine(string line, out Regex? matched)
    {
        foreach (var pattern in Patterns)
        {
            var entry = TryParseWithPattern(line, pattern);
            if (entry != null)
            {
                matched = pattern;
                return entry;
            }
        }
        matched = null;
        return null;
    }

    private static LogEntry? TryParseWithPattern(string line, Regex pattern)
    {
        var match = pattern.Match(line);
        if (!match.Success) return null;

        var tsStr = match.Groups["ts"].Value.Replace(',', '.');
        DateTime.TryParse(tsStr, out var timestamp);

        var level = NormalizeLevel(match.Groups["level"].Value);
        var source = match.Groups["source"].Success ? match.Groups["source"].Value : "";
        var message = match.Groups["msg"].Success ? match.Groups["msg"].Value.Trim() : "";

        if (string.IsNullOrEmpty(message))
            message = "(empty)";

        string? correlationId = null;
        var corrMatch = CorrelationIdPattern.Match(line);
        if (corrMatch.Success)
            correlationId = corrMatch.Groups[1].Value.Trim();

        return new LogEntry
        {
            Timestamp = timestamp,
            Level = level,
            Source = source.Length > 200 ? source[..200] : source,
            Message = message.Length > 2000 ? message[..2000] : message,
            CorrelationId = correlationId,
            RawLine = line.Length > 500 ? line[..500] : line
        };
    }

    private static string NormalizeLevel(string level)
    {
        return level.ToUpperInvariant() switch
        {
            "ERR" or "ERROR" => "ERROR",
            "WRN" or "WARN" or "WARNING" => "WARN",
            "INF" or "INFO" or "INFORMATION" => "INFO",
            "DBG" or "DEBUG" => "DEBUG",
            "FTL" or "FATAL" or "CRITICAL" => "FATAL",
            "TRC" or "TRACE" or "VERBOSE" or "VRB" => "TRACE",
            _ => level.ToUpperInvariant()
        };
    }
}
