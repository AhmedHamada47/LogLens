using System.ComponentModel.DataAnnotations;

namespace LogLens.Models;

public class LogEntry
{
    public int Id { get; set; }

    public DateTime Timestamp { get; set; }

    [Required]
    public string Level { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    [Required]
    public string Message { get; set; } = string.Empty;

    public string? StackTrace { get; set; }

    public string? CorrelationId { get; set; }

    public string? RawLine { get; set; }

    public int SessionId { get; set; }
    public LogSession Session { get; set; } = null!;
}
