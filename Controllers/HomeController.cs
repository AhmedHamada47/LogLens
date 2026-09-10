using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LogLens.Data;
using LogLens.Models;

namespace LogLens.Controllers;

public class HomeController : Controller
{
    private readonly AppDbContext _db;

    public HomeController(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var sessions = await _db.LogSessions
            .AsNoTracking()
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new SessionSummaryViewModel
            {
                Id = s.Id,
                Name = s.Name,
                CreatedAt = s.CreatedAt,
                SourceFileName = s.SourceFileName,
                TotalCount = s.Entries.Count,
                ErrorCount = s.Entries.Count(e => e.Level == "ERROR" || e.Level == "FATAL"),
                WarnCount = s.Entries.Count(e => e.Level == "WARN")
            })
            .ToListAsync();

        return View(sessions);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
