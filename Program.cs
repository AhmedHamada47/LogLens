using Microsoft.EntityFrameworkCore;
using LogLens.Data;
using LogLens.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews(options =>
{
    options.MaxModelBindingCollectionSize = int.MaxValue;
});

builder.WebHost.ConfigureKestrel(options =>
{
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
    options.UseSqlite("Data Source=loglens.db"));

builder.Services.AddScoped<LogParserService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();

    var conn = db.Database.GetDbConnection();
    conn.Open();
    using var cmd = conn.CreateCommand();
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
    conn.Close();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
