using System.Diagnostics;
using System.Net;
using Microsoft.EntityFrameworkCore;
using LogLens.Data;
using LogLens.Desktop;
using LogLens.Services;

const int DefaultPort = 5189;

// --- Single instance check ---
var mutexName = "LogLens_SingleInstance";
using var mutex = new Mutex(true, mutexName, out bool isNewInstance);
if (!isNewInstance)
{
    OpenBrowser($"http://127.0.0.1:{DefaultPort}");
    return;
}

// --- SQLite in AppData ---
var appDataDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "LogLens");
Directory.CreateDirectory(appDataDir);
var dbPath = Path.Combine(appDataDir, "loglens.db");

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews(options =>
{
    options.MaxModelBindingCollectionSize = int.MaxValue;
});

// --- Bind to localhost only ---
builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(IPAddress.Loopback, DefaultPort);
    options.Limits.MaxRequestBodySize = 500L * 1024 * 1024;
    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(5);
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(5);
});

builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 500L * 1024 * 1024;
    options.ValueLengthLimit = 50 * 1024 * 1024;
    options.ValueCountLimit = 5000;
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

builder.Services.AddSingleton<LogParserService>();

builder.Services.AddResponseCompression();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();

    var conn = db.Database.GetDbConnection();
    conn.Open();
    using var cmd = conn.CreateCommand();

    cmd.CommandText = "CREATE TABLE IF NOT EXISTS SchemaVersion (Version INTEGER NOT NULL)";
    cmd.ExecuteNonQuery();

    cmd.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM SchemaVersion";
    var schemaVersion = Convert.ToInt32(cmd.ExecuteScalar());

    if (schemaVersion < 1)
    {
        cmd.CommandText = @"
            CREATE INDEX IF NOT EXISTS IX_LogEntries_SessionId_Timestamp ON LogEntries(SessionId, Timestamp);
            CREATE INDEX IF NOT EXISTS IX_LogEntries_SessionId_Level ON LogEntries(SessionId, Level);
        ";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "PRAGMA table_info(LogEntries)";
        bool hasCorrelationId = false;
        using (var rdr = cmd.ExecuteReader())
        {
            while (rdr.Read())
                if (rdr.GetString(1) == "CorrelationId") hasCorrelationId = true;
        }
        if (!hasCorrelationId)
        {
            cmd.CommandText = "ALTER TABLE LogEntries ADD COLUMN CorrelationId TEXT";
            cmd.ExecuteNonQuery();
        }

        cmd.CommandText = @"
            CREATE INDEX IF NOT EXISTS IX_LogEntries_SessionId_CorrelationId ON LogEntries(SessionId, CorrelationId);
        ";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "INSERT INTO SchemaVersion (Version) VALUES (1)";
        cmd.ExecuteNonQuery();
    }

    conn.Close();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseResponseCompression();

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; font-src https://fonts.gstatic.com; script-src 'self' 'unsafe-inline'; img-src 'self' data:;";
    await next();
});

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers["Cache-Control"] = "public, max-age=604800";
    }
});
app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// --- Auto-open browser after server starts ---
app.Lifetime.ApplicationStarted.Register(() =>
{
    OpenBrowser($"http://127.0.0.1:{DefaultPort}");
});

// --- Tray icon (the app has no console window) ---
TrayIcon.Start(app.Lifetime, $"http://127.0.0.1:{DefaultPort}", OpenBrowser);

try
{
    app.Run();
}
catch (Exception ex)
{
    TrayIcon.ShowError($"LogLens could not start.{Environment.NewLine}{Environment.NewLine}{ex.Message}");
}

// --- Helper ---
static void OpenBrowser(string url)
{
    try
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
    catch
    {
    }
}
