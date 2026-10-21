# LogLens

A lightweight, self-contained desktop log viewer for Serilog structured logs. Built with ASP.NET Core 9.0 and SQLite.

## Download

**[⬇ Download LogLens for Windows](https://github.com/AhmedHamada47/LogLens/releases/latest/download/LogLens-Setup.exe)**

1. Run `LogLens-Setup.exe` (no admin rights needed).
2. If Windows SmartScreen shows *"Windows protected your PC"*, click **More info → Run anyway** (the app is not code-signed).
3. Double-click the **LogLens** icon on your desktop. It opens in your browser and lives in the system tray — right-click the tray icon → **Exit** to close it.

Requires Windows 10/11 (64-bit). Nothing else to install.

## Features

- **Drag & Drop Upload** — Import `.log`, `.txt`, or `.json` files up to 500 MB, or paste log content directly
- **Virtual Scrolling** — Smoothly browse 79K+ log entries without lag
- **Smart Filtering** — Filter by level, time range, text search, or regex patterns
- **Request Tracing** — Follow correlation IDs across related log entries
- **Activity Timeline** — Visual chart showing log volume over time with error/warn breakdown
- **Top Errors Summary** — Quickly identify the most frequent errors
- **Stack Trace Viewer** — Formatted, collapsible call stacks with app-frame highlighting
- **Export** — Download filtered results as CSV or JSON (streaming, handles large datasets)
- **Keyboard Shortcuts** — Vim-style navigation (j/k, g/G), quick filters (E, W, R), search (/)
- **Dark/Light Theme** — Toggle between themes with persistent preference
- **Single Instance** — Mutex-based guard prevents duplicate server processes
- **Localhost Only** — Binds to 127.0.0.1 for security; no network exposure

## Tech Stack

- **Runtime:** .NET 9.0 (self-contained, win-x64)
- **Framework:** ASP.NET Core MVC
- **Database:** SQLite via EF Core (stored in %LOCALAPPDATA%\LogLens)
- **Frontend:** Vanilla JS, Bootstrap 5, Inter + JetBrains Mono fonts
- **Testing:** xUnit (11 tests)

## Getting Started

### Run in Development

```bash
dotnet run
```

The app opens automatically at `http://127.0.0.1:5189`.

### Publish for Production

```bash
dotnet publish -c Release
```

Produces a self-contained executable in `bin/Release/net9.0/win-x64/publish/`.

### Build the installer

Run `publish.bat` (needs [Inno Setup 6](https://jrsoftware.org/isdl.php): `winget install JRSoftware.InnoSetup`). Output: `installer/output/LogLens-Setup.exe`.

### Release a new version

Push a tag like `v1.0.1`. The GitHub Actions workflow builds the installer and attaches it to a GitHub Release, so the download link above always points to the newest version.

## Security

- CSRF protection via `[AutoValidateAntiforgeryToken]`
- XSS prevention with server-side `JsonSerializer.Serialize` for all JS interpolation
- Content Security Policy, X-Frame-Options, and X-Content-Type-Options headers
- Input validation with `[MaxLength]` attributes and server-side checks
- File extension allowlist (`.log`, `.txt`, `.json`)

## Log Format Support

Parses standard Serilog output patterns:

- `yyyy-MM-dd HH:mm:ss.fff [Level] Source: Message`
- `yyyy-MM-dd HH:mm:ss [Level] Message`
- `[HH:mm:ss Level] Message`
- JSON-structured Serilog output

## License

MIT
