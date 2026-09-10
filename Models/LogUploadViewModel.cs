using Microsoft.AspNetCore.Http;

namespace LogLens.Models;

public class LogUploadViewModel
{
    public string? PastedLogs { get; set; }
    public IFormFile? LogFile { get; set; }
    public string SessionName { get; set; } = string.Empty;
}
