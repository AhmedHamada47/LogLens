namespace LogLens.Models;

public class LogSession
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? SourceFileName { get; set; }
    public List<LogEntry> Entries { get; set; } = new();
}
