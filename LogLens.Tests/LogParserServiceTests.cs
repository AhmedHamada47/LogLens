using System.Text;
using LogLens.Services;

namespace LogLens.Tests;

public class LogParserServiceTests
{
    private readonly LogParserService _parser = new();

    private List<LogLens.Models.LogEntry> Parse(string text)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        return _parser.ParseStreamEnumerable(stream).ToList();
    }

    [Fact]
    public void ParsesSerilogWithTimezone()
    {
        var entries = Parse("2026-09-07 20:57:44.041 +02:00 [INF] [Microsoft.Hosting] Application started");
        Assert.Single(entries);
        Assert.Equal("INFO", entries[0].Level);
        Assert.Equal("Microsoft.Hosting", entries[0].Source);
        Assert.Contains("Application started", entries[0].Message);
    }

    [Fact]
    public void ParsesSerilogWithoutTimezone()
    {
        var entries = Parse("2024-01-15 10:30:45.123 [ERR] MyApp.Service - Something failed");
        Assert.Single(entries);
        Assert.Equal("ERROR", entries[0].Level);
        Assert.Equal("MyApp.Service", entries[0].Source);
    }

    [Fact]
    public void ParsesStandardFormat()
    {
        var entries = Parse("2024-01-15 10:30:45 ERROR MyApp.Controller - Request failed");
        Assert.Single(entries);
        Assert.Equal("ERROR", entries[0].Level);
    }

    [Fact]
    public void ParsesBracketedFormat()
    {
        var entries = Parse("[2024-01-15 10:30:45] [WARN] Something is slow");
        Assert.Single(entries);
        Assert.Equal("WARN", entries[0].Level);
    }

    [Fact]
    public void NormalizesLevelAbbreviations()
    {
        var entries = Parse(
            "2024-01-15 10:30:45 [WRN] MyApp - warn msg\n" +
            "2024-01-15 10:30:46 [FTL] MyApp - fatal msg\n" +
            "2024-01-15 10:30:47 [DBG] MyApp - debug msg\n" +
            "2024-01-15 10:30:48 [TRC] MyApp - trace msg");
        Assert.Equal(4, entries.Count);
        Assert.Equal("WARN", entries[0].Level);
        Assert.Equal("FATAL", entries[1].Level);
        Assert.Equal("DEBUG", entries[2].Level);
        Assert.Equal("TRACE", entries[3].Level);
    }

    [Fact]
    public void AttachesStackTraceToEntry()
    {
        var text = "2024-01-15 10:30:45 [ERR] MyApp - Crash\n" +
                   "   at MyApp.Service.DoWork() in C:\\code\\Service.cs:line 42\n" +
                   "   at MyApp.Program.Main()";
        var entries = Parse(text);
        Assert.Single(entries);
        Assert.NotNull(entries[0].StackTrace);
        Assert.Contains("DoWork", entries[0].StackTrace);
        Assert.Contains("Main", entries[0].StackTrace);
    }

    [Fact]
    public void ExtractsCorrelationId()
    {
        var entries = Parse("2026-09-07 20:57:44.041 +02:00 [INF] [MyApp] [CorrelationId: abc-123] Request started");
        Assert.Single(entries);
        Assert.Equal("abc-123", entries[0].CorrelationId);
    }

    [Fact]
    public void HandlesMultipleEntries()
    {
        var text = "2024-01-15 10:30:45 [INF] App - First\n" +
                   "2024-01-15 10:30:46 [INF] App - Second\n" +
                   "2024-01-15 10:30:47 [ERR] App - Third";
        var entries = Parse(text);
        Assert.Equal(3, entries.Count);
    }

    [Fact]
    public void HandlesEmptyInput()
    {
        var entries = Parse("");
        Assert.Empty(entries);
    }

    [Fact]
    public void TruncatesLongMessages()
    {
        var longMsg = new string('x', 3000);
        var entries = Parse($"2024-01-15 10:30:45 [INF] App - {longMsg}");
        Assert.Single(entries);
        Assert.True(entries[0].Message.Length <= 2000);
    }

    [Fact]
    public void TruncatesLongSource()
    {
        var longSrc = new string('S', 300);
        var entries = Parse($"2024-01-15 10:30:45 [INF] {longSrc} - msg");
        Assert.Single(entries);
        Assert.True(entries[0].Source.Length <= 200);
    }
}
