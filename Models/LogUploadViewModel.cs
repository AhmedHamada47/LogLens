using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace LogLens.Models;

public class LogUploadViewModel
{
    [MaxLength(10_000_000)]
    public string? PastedLogs { get; set; }

    public IFormFile? LogFile { get; set; }

    // Optional: defaults to the uploaded file name (or a timestamp for pasted logs)
    [MaxLength(200)]
    public string? SessionName { get; set; }
}
