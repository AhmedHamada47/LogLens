# LogLens

A fast, private desktop log viewer for Windows, built for Serilog-style logs. Drop in a log file and get filtering, search, error summaries and request tracing, all on your own PC.

Built with ASP.NET Core 9 MVC and SQLite, packaged as a desktop app with its own icon, tray icon and installer.

## Download

**[⬇ Download LogLens for Windows](https://github.com/AhmedHamada47/LogLens/releases/latest/download/LogLens-Setup.exe)**

1. Run `LogLens-Setup.exe`. No admin rights are needed.
2. If Windows SmartScreen shows *"Windows protected your PC"*, click **More info → Run anyway** (the app is not code-signed).
3. Double-click the **LogLens** icon on your desktop. It opens in your browser.
4. LogLens keeps running in the **system tray** (near the clock):
   - double-click the tray icon to reopen the page
   - right-click it → **Exit** to close LogLens

Requires Windows 10/11 (64-bit). Nothing else to install; the .NET runtime is included.

## Using LogLens

1. Click **+ Upload Logs**, then drag and drop a `.log`, `.txt` or `.json` file (up to 500 MB) or paste log text. **Session Name** is optional and defaults to the file name.
2. Click **Parse & Analyze**. A 47 MB log (~216,000 entries) takes a few seconds.
3. In the session view:
   - filter by **ERROR / WARN / INFO**, search text or regex, and limit by time range
   - open **Insights** for the activity timeline and the top errors
   - click **[stack]** to see a formatted stack trace, or **Trace** to see every entry from the same request (correlation ID)
   - export the current view as **CSV** or **JSON**

| Key | Action |
| --- | --- |
| `/` | Search |
| `E` / `W` | Show errors / warnings |
| `R` | Reset filters |
| `J` / `K` | Scroll down / up |
| `G` / `Shift+G` | Jump to top / bottom |
| `?` | Show all shortcuts |

Your sessions are stored in `%LOCALAPPDATA%\LogLens\loglens.db`. Uninstalling keeps them; delete that folder to remove all data.

## Features

- **Big files, fast**: streams the file line by line and bulk-inserts in one SQLite transaction (47 MB in ~4–7 s)
- **Virtual scrolling**: smooth scrolling through hundreds of thousands of rows
- **Smart filtering**: level, time range, text search or regex
- **Request tracing**: follow a correlation ID across related entries
- **Insights**: per-minute activity timeline and top-10 errors
- **Stack trace viewer**: collapsible, formatted call stacks
- **Streaming export**: CSV or JSON without loading everything into memory
- **Dark / light theme** and **compact** row density
- **Private by design**: listens on `127.0.0.1` only; nothing leaves your PC
- **Single instance**: opening it again just brings up the running app

## Supported log formats

The format is detected automatically from the first lines of the file:

```text
2026-10-21 04:13:37.060 +02:00 [INF] [Source] [CorrelationId:abc] Message   ← Serilog with time zone
2026-10-21 04:13:37 [Information] Source: Message                           ← Serilog without time zone
2026-10-21 04:13:37 ERROR Source - Message                                   ← plain level
[2026-10-21T04:13:37] [WARN] Message                                         ← bracketed
```

Lines without a timestamp are attached to the previous entry (stack traces, long SQL, and so on). Short level names such as `INF`, `WRN`, `ERR`, `DBG`, `FTL` and `VRB` are recognized.

## How it works

LogLens is a regular ASP.NET Core MVC web app running inside a Windows desktop shell:

- `LogLens.exe` is a windowed app (`WinExe`) that starts a local web server on `http://127.0.0.1:5189` and opens it in your default browser
- a tray icon (`Desktop/TrayIcon.cs`) replaces the console window
- the app is published **self-contained** and wrapped by an **Inno Setup** installer that adds desktop and Start Menu shortcuts

For a full walkthrough (every file, the icon, the installer, the parsing pipeline, and each problem solved along the way), open [Docs/LogLens-Desktop-Guide.html](Docs/LogLens-Desktop-Guide.html).

## Development

**Requirements:** .NET SDK 9 or later, Windows.

```bash
dotnet run
```

The app opens at `http://127.0.0.1:5189`.

```bash
dotnet test LogLens.Tests/LogLens.Tests.csproj
```

### Build the installer locally

```bash
winget install JRSoftware.InnoSetup
```

Then run `publish.bat`. It publishes the app and builds `installer/output/LogLens-Setup.exe`.

### Release a new version

```bash
git tag -a v1.0.1 -m "LogLens 1.0.1"
git push origin v1.0.1
```

The **Release** GitHub Actions workflow runs the tests, builds the installer and attaches it to a **draft** GitHub Release. Download and test the draft, then click **Publish release**. The download link above always points to the newest published release.

### Change the icon

Replace `LogLens.ico` (include 16, 32, 48 and 256 px sizes) and rebuild. The exe, shortcuts, tray icon and installer all use it.

## Project structure

| Path | Purpose |
| --- | --- |
| `Program.cs` | Startup: single instance, local server, database setup, security headers, browser and tray |
| `Desktop/TrayIcon.cs` | Tray icon (Open / Exit) and startup error dialog |
| `Controllers/` | Dashboard, upload, session view, entries API, trace, export, delete |
| `Services/LogParserService.cs` | Streaming log parser |
| `Data/AppDbContext.cs` | SQLite schema and indexes |
| `Models/` | Sessions, entries and view models |
| `Views/`, `wwwroot/` | Razor pages, CSS and JavaScript (including the virtual scroller) |
| `installer/LogLens.iss` | Inno Setup installer script |
| `.github/workflows/release.yml` | Builds draft releases on `v*` tags |
| `LogLens.Tests/` | xUnit tests for the parser |
| `Docs/LogLens-Desktop-Guide.html` | Full technical guide |

## Security

- Listens on loopback (`127.0.0.1`) only
- CSRF protection via `[AutoValidateAntiforgeryToken]`
- Content Security Policy, `X-Frame-Options` and `X-Content-Type-Options` headers
- Server-side JSON encoding for all values written into scripts
- Input validation and an upload extension allowlist (`.log`, `.txt`, `.json`)

## Troubleshooting

| Problem | Fix |
| --- | --- |
| "Windows protected your PC" | Click **More info → Run anyway**. |
| A page looks unstyled after updating | Press **Ctrl+F5** once to refresh cached files. |
| "LogLens could not start" | Another program is using port 5189. Close it, or change `DefaultPort` in `Program.cs`. |
| Nothing happens when opening LogLens | It's already running: look for the tray icon, or open `http://127.0.0.1:5189`. |

## License

MIT
