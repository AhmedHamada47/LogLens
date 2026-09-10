namespace LogLens.Models;

public class SessionSummaryViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? SourceFileName { get; set; }
    public int TotalCount { get; set; }
    public int ErrorCount { get; set; }
    public int WarnCount { get; set; }
}
