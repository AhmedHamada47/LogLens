using System.Text.RegularExpressions;
using LogLens.Models;

namespace LogLens.Services;

public class LogParserService
{
    // Serilog with timezone: 2026-09-07 20:57:44.041 +02:00 [INF] [Source] [extras]... Message
    private static readonly Regex SerilogTz = new(
        @"^(?<ts>\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}(?:[\.,]\d+)?)\s+[+-]\d{2}:\d{2}\s+\[(?<level>[A-Za-z]+)\]\s+\[(?<source>[^\]]+)\](?:\s*\[[^\]]*\])*\s*(?<msg>.*)",
        RegexOptions.Compiled);

    // Serilog no timezone: 2024-01-15 10:30:45.123 [ERR] Source - Message
    private static readonly Regex SerilogNoTz = new(
        @"^(?<ts>\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}(?:[\.,]\d+)?)\s+\[(?<level>[A-Za-z]+)\]\s+(?<source>\S+)?\s*[-:]?\s*(?<msg>.+)",
        RegexOptions.Compiled);

    // Standard: 2024-01-15 10:30:45 ERROR Source - Message
    private static readonly Regex Standard = new(
        @"^(?<ts>\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}(?:[\.,]\d+)?)\s+(?<level>TRACE|DEBUG|INFO|WARN|WARNING|ERROR|FATAL|CRITICAL|ERR|WRN|INF|DBG|FTL|TRC|VRB)\s+(?<source>\S+)?\s*[-:]?\s*(?<msg>.+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Bracketed: [2024-01-15 10:30:45] [Error] message
    private static readonly Regex Bracketed = new(
        @"^\[?(?<ts>\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}(?:[\.,]\d+)?)\]?\s*\[?(?<level>TRACE|DEBUG|INFO|WARN|WARNING|ERROR|FATAL|CRITICAL|ERR|WRN|INF|DBG|FTL|TRC|VRB)\]?\s*(?<msg>.+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex[] Patterns = { SerilogTz, SerilogNoTz, Standard, Bracketed };

    private static readonly Regex CorrelationIdPattern = new(
        @"\[CorrelationId[:\s]*([^\]]+)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
            else if (current != null && continuationCount < 20)
            {
                continuationCount++;
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("at ") || trimmed.StartsWith("---") || trimmed.StartsWith("Exception"))
                {
                    current.StackTrace = string.IsNullOrEmpty(current.StackTrace)
                        ? line : current.StackTrace + "\n" + line;
                }
                else
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

    public List<LogEntry> Parse(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return [];

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(rawText));
        return ParseStream(stream);
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
