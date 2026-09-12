# LogLens — Personal Serilog Log Viewer

**Stack**: ASP.NET Core 9.0 MVC + SQLite + Bootstrap + Custom Dark Theme

---

## Architecture

| Layer | Files |
|-------|-------|
| **Controllers** | `HomeController.cs` (dashboard), `LogsController.cs` (upload, view, trace, export, delete) |
| **Services** | `LogParserService.cs` — parses 4 log formats (Serilog TZ/no-TZ, Standard, Bracketed), auto-detects format, extracts stack traces + correlation IDs |
| **Models** | `LogEntry`, `LogSession`, + 5 ViewModels |
| **Database** | SQLite via EF Core, 2 tables (LogEntries, LogSessions), 3 indexes, cascade delete |
| **Views** | Dashboard (session cards), Upload (drag-drop + paste), View (full log viewer) |

---

## API Endpoints

| Method | Route | Purpose |
|--------|-------|---------|
| GET | `/` | Dashboard — all sessions with stats |
| GET/POST | `/Logs/Upload` | Upload log file or paste text |
| GET | `/Logs/View/{id}` | Full log viewer with filters |
| GET | `/Logs/EntriesApi/{id}` | JSON API for virtual scrolling (offset/limit) |
| GET | `/Logs/Trace/{id}?entryId=` | Correlated entries for tracing |
| GET | `/Logs/Export/{id}?format=csv|json` | Export filtered logs |
| POST | `/Logs/Delete/{id}` | Delete a session |

---

## Frontend Features

- **Virtual scrolling** — renders ~15 visible rows out of 79K+, fetches via JSON API
- **Regex search** with SQLite custom function
- **Click-to-expand** rows showing full message, source, stack trace
- **Call stack renderer** — highlights app frames vs framework frames
- **Canvas timeline chart** — activity over time by level
- **Trace panel** — slide-in side panel showing correlated entries
- **Keyboard shortcuts** — `/` search, `J/K` navigate, `G`/`Shift+G` top/bottom, `?` help
- **Dark/Light theme** toggle (localStorage persisted)
- **Density toggle** — compact vs comfortable row heights
- **Drag-and-drop** file upload

---

## Key Configuration

- 500MB max upload size
- 5 min request timeouts
- No auth — personal tool
- SQLite file: `loglens.db`

---

## Dependencies

- `Microsoft.EntityFrameworkCore.Sqlite` 9.0.15
- `Microsoft.EntityFrameworkCore.Tools` 9.0.15
- Bootstrap + jQuery (bundled in wwwroot/lib)

---

## Custom CSS

~1,560 lines of hand-written CSS with a full design system: CSS variables for theming, glassmorphism cards, color-coded log levels, Inter + JetBrains Mono fonts, responsive layout.

---

## Project Structure

```
LogLens/
├── Controllers/
│   ├── HomeController.cs
│   └── LogsController.cs
├── Data/
│   └── AppDbContext.cs
├── Models/
│   ├── ErrorViewModel.cs
│   ├── LogEntry.cs
│   ├── LogFilterViewModel.cs
│   ├── LogSession.cs
│   ├── LogUploadViewModel.cs
│   └── SessionSummaryViewModel.cs
├── Services/
│   └── LogParserService.cs
├── Views/
│   ├── Home/
│   │   ├── Index.cshtml
│   │   └── Privacy.cshtml
│   ├── Logs/
│   │   ├── Upload.cshtml
│   │   └── View.cshtml
│   └── Shared/
│       ├── _Layout.cshtml
│       ├── Error.cshtml
│       └── _ValidationScriptsPartial.cshtml
├── wwwroot/
│   ├── css/
│   │   └── site.css
│   └── lib/
│       ├── bootstrap/
│       ├── jquery/
│       └── jquery-validation/
├── Program.cs
├── LogLens.csproj
└── appsettings.json
```
