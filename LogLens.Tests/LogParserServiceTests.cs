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
    public void EmptyCorrelationIdIsNull()
    {
        var entries = Parse("2026-09-07 20:57:44.041 +02:00 [INF] [MyApp] [CorrelationId:] [ThreadId:2] Startup");
        Assert.Single(entries);
        Assert.Null(entries[0].CorrelationId);
    }

    [Fact]
    public void ExtractsCorrelationIdWithoutSpace()
    {
        var entries = Parse("2026-09-07 20:57:44.041 +02:00 [ERR] [MyApp] [CorrelationId:e354bb49850244e3] Failed");
        Assert.Single(entries);
        Assert.Equal("e354bb49850244e3", entries[0].CorrelationId);
    }

    [Fact]
    public void KeepsLongStackTraces()
    {
        var frames = string.Join("\n", Enumerable.Range(1, 60).Select(i => $"   at App.Frame{i}() in C:\\src\\App.cs:line {i}"));
        var entries = Parse("2026-09-07 20:57:44.041 +02:00 [ERR] [MyApp] Failed\n" + frames);
        Assert.Single(entries);
        Assert.Contains("Frame60", entries[0].StackTrace);
    }

    [Fact]
    public void KeepsExceptionChainInStackTrace()
    {
        var text = "2026-09-07 20:57:44.041 +02:00 [ERR] [MyApp] Request failed\n" +
                   "System.InvalidOperationException: Outer problem\n" +
                   " ---> Microsoft.Data.SqlClient.SqlException (0x80131904): Inner problem\n" +
                   "   at Microsoft.Data.SqlClient.SqlCommand.Execute()\n" +
                   "   --- End of inner exception stack trace ---\n" +
                   "   at App.Service.Run() in C:\\src\\Service.cs:line 42";
        var entries = Parse(text);
        Assert.Single(entries);
        var stack = entries[0].StackTrace!;
        Assert.Contains("System.InvalidOperationException: Outer problem", stack);
        Assert.Contains("---> Microsoft.Data.SqlClient.SqlException", stack);
        Assert.Contains("--- End of inner exception stack trace ---", stack);
        Assert.Contains("Service.cs:line 42", stack);
        // the exception text stays searchable in the message
        Assert.Contains("InvalidOperationException", entries[0].Message);
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
