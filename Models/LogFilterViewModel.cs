namespace LogLens.Models;

public class LogFilterViewModel
{
    public int SessionId { get; set; }
    public string? SearchTerm { get; set; }
    public string? Level { get; set; }
    public string? CorrelationId { get; set; }
    public string? FromTime { get; set; }
    public string? ToTime { get; set; }
    public string SortBy { get; set; } = "Timestamp";
    public string SortOrder { get; set; } = "desc";
    public bool UseRegex { get; set; }

    public LogSession Session { get; set; } = null!;
    public Dictionary<string, int> LevelCounts { get; set; } = new();
    public int TotalCount { get; set; }
    public int FilteredCount { get; set; }
    public List<ErrorSummaryItem> ErrorSummary { get; set; } = new();
    public List<TimelineBucket> Timeline { get; set; } = new();
}

public class ErrorSummaryItem
{
    public string Message { get; set; } = "";
    public string Level { get; set; } = "";
    public int Count { get; set; }
    public DateTime LastSeen { get; set; }
}

public class TimelineBucket
{
    public string Time { get; set; } = "";
    public int Info { get; set; }
    public int Warn { get; set; }
    public int Error { get; set; }
}
