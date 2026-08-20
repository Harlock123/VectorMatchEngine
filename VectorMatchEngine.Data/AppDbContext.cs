using Microsoft.EntityFrameworkCore;
using VectorMatchEngine.Data.Entities;

namespace VectorMatchEngine.Data;

public class AppDbContext : DbContext
{
    public DbSet<Dataset> Datasets => Set<Dataset>();
    public DbSet<DatasetRecord> DatasetRecords => Set<DatasetRecord>();
    public DbSet<MatchJob> MatchJobs => Set<MatchJob>();
    public DbSet<MatchResult> MatchResults => Set<MatchResult>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ── Dataset ──────────────────────────────────────────────────────
        modelBuilder.Entity<Dataset>().Property(d => d.Name).HasMaxLength(256);
        modelBuilder.Entity<Dataset>().Property(d => d.FileName).HasMaxLength(512);
        modelBuilder.Entity<Dataset>().Property(d => d.VectorizedColumnsJson).HasColumnType("NVARCHAR(MAX)");
        modelBuilder.Entity<Dataset>().Property(d => d.PreservedColumnsJson).HasColumnType("NVARCHAR(MAX)");

        // ── DatasetRecord ────────────────────────────────────────────────
        modelBuilder.Entity<DatasetRecord>().Property(r => r.VectorData).HasColumnType("VARBINARY(MAX)");
        modelBuilder.Entity<DatasetRecord>().Property(r => r.PreservedDataJson).HasColumnType("NVARCHAR(MAX)");

        // Deleting a dataset removes its records.
        modelBuilder.Entity<DatasetRecord>()
            .HasOne(r => r.Dataset)
            .WithMany(d => d.Records)
            .HasForeignKey(r => r.DatasetId)
            .OnDelete(DeleteBehavior.Cascade);

        // ── MatchJob ─────────────────────────────────────────────────────
        modelBuilder.Entity<MatchJob>().Property(j => j.Status).HasMaxLength(50);

        // Restrict, not Cascade: two FKs into Datasets would otherwise create multiple
        // cascade paths, which SQL Server rejects outright. Restricting also enforces the
        // rule that a dataset still referenced by a job cannot be deleted.
        modelBuilder.Entity<MatchJob>()
            .HasOne(j => j.DatasetA)
            .WithMany()
            .HasForeignKey(j => j.DatasetAId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<MatchJob>()
            .HasOne(j => j.DatasetB)
            .WithMany()
            .HasForeignKey(j => j.DatasetBId)
            .OnDelete(DeleteBehavior.Restrict);

        // ── MatchResult ──────────────────────────────────────────────────
        // Deleting a job removes its results ...
        modelBuilder.Entity<MatchResult>()
            .HasOne(r => r.MatchJob)
            .WithMany(j => j.Results)
            .HasForeignKey(r => r.MatchJobId)
            .OnDelete(DeleteBehavior.Cascade);

        // ... but the record references are Restrict for the same multiple-cascade-path reason.
        modelBuilder.Entity<MatchResult>()
            .HasOne(r => r.RecordA)
            .WithMany()
            .HasForeignKey(r => r.RecordAId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<MatchResult>()
            .HasOne(r => r.RecordB)
            .WithMany()
            .HasForeignKey(r => r.RecordBId)
            .OnDelete(DeleteBehavior.Restrict);

        // ── Indexes for performance ──────────────────────────────────────
        modelBuilder.Entity<DatasetRecord>().HasIndex(r => r.DatasetId);
        modelBuilder.Entity<MatchResult>().HasIndex(r => r.MatchJobId);
        modelBuilder.Entity<MatchResult>().HasIndex(r => r.RecordAId);
        modelBuilder.Entity<MatchResult>().HasIndex(r => r.RecordBId);
        modelBuilder.Entity<MatchJob>().HasIndex(j => j.DatasetAId);
        modelBuilder.Entity<MatchJob>().HasIndex(j => j.DatasetBId);
    }
}
