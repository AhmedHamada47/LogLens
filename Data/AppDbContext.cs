using Microsoft.EntityFrameworkCore;
using LogLens.Models;

namespace LogLens.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<LogEntry> LogEntries => Set<LogEntry>();
    public DbSet<LogSession> LogSessions => Set<LogSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LogEntry>()
            .HasIndex(e => new { e.SessionId, e.Timestamp })
            .HasDatabaseName("IX_LogEntries_SessionId_Timestamp");

        modelBuilder.Entity<LogEntry>()
            .HasIndex(e => new { e.SessionId, e.Level })
            .HasDatabaseName("IX_LogEntries_SessionId_Level");

        modelBuilder.Entity<LogEntry>()
            .HasIndex(e => new { e.SessionId, e.CorrelationId })
            .HasDatabaseName("IX_LogEntries_SessionId_CorrelationId");

        modelBuilder.Entity<LogEntry>()
            .HasIndex(e => e.Timestamp);

        modelBuilder.Entity<LogEntry>()
            .HasIndex(e => e.Level);

        modelBuilder.Entity<LogEntry>()
            .HasOne(e => e.Session)
            .WithMany(s => s.Entries)
            .HasForeignKey(e => e.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
