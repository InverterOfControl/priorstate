using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PriorState.Domain.Entities;

namespace PriorState.Data.Configurations;

internal sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.SeedUrls).HasColumnType("jsonb").IsRequired();
        builder.Property(p => p.ScopeIncludes).HasColumnType("jsonb").IsRequired();
        builder.Property(p => p.ScopeExcludes).HasColumnType("jsonb").IsRequired();
        builder.Property(p => p.Schedule).HasMaxLength(120);
        builder.Property(p => p.RetentionYears).IsRequired();

        builder.HasOne(p => p.CaptureProfileVersion)
            .WithMany()
            .HasForeignKey(p => p.CaptureProfileVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => p.Name).IsUnique();
    }
}

internal sealed class CaptureProfileVersionConfiguration : IEntityTypeConfiguration<CaptureProfileVersion>
{
    public void Configure(EntityTypeBuilder<CaptureProfileVersion> builder)
    {
        builder.ToTable("capture_profile_versions");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).HasMaxLength(120).IsRequired();
        builder.Property(p => p.Version).IsRequired();
        builder.Property(p => p.Rationale).HasMaxLength(2000).IsRequired();

        // A (name, version) pair identifies a profile for all time and appears verbatim in every
        // evidence package. Reusing one for different settings would make old protocols lie.
        builder.HasIndex(p => new { p.Name, p.Version })
            .IsUnique()
            .HasDatabaseName("ix_capture_profile_versions_name_version");

        builder.ComplexProperty(p => p.Conditions, conditions =>
        {
            conditions.Property(c => c.UserAgent).HasMaxLength(512).IsRequired();
            conditions.Property(c => c.CookieBanner).HasConversion<string>().HasMaxLength(32).IsRequired();
            conditions.Property(c => c.ChromiumVersion).HasMaxLength(64).IsRequired();
            conditions.Property(c => c.CrawlerVersion).HasMaxLength(64).IsRequired();
        });
    }
}

internal sealed class RunConfiguration : IEntityTypeConfiguration<Run>
{
    public void Configure(EntityTypeBuilder<Run> builder)
    {
        builder.ToTable("runs");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Trigger).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(r => r.FailureReason).HasMaxLength(4000);

        // Recorded verbatim so a third party can reproduce the capture by hand.
        builder.Property(r => r.CrawlerArguments).HasColumnType("jsonb").IsRequired();

        builder.HasOne(r => r.Project)
            .WithMany()
            .HasForeignKey(r => r.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.CaptureProfileVersion)
            .WithMany()
            .HasForeignKey(r => r.CaptureProfileVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => new { r.ProjectId, r.QueuedAt }).HasDatabaseName("ix_runs_project_queued_at");
    }
}

internal sealed class CrawlJobConfiguration : IEntityTypeConfiguration<CrawlJob>
{
    public void Configure(EntityTypeBuilder<CrawlJob> builder)
    {
        builder.ToTable("crawl_jobs");
        builder.HasKey(j => j.Id);

        builder.Property(j => j.State).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(j => j.ClaimedBy).HasMaxLength(200);
        builder.Property(j => j.LastError).HasMaxLength(4000);

        builder.HasOne(j => j.Run)
            .WithMany()
            .HasForeignKey(j => j.RunId)
            .OnDelete(DeleteBehavior.Cascade);

        // The claim query orders by AvailableAt over pending rows; this index is what keeps
        // FOR UPDATE SKIP LOCKED cheap as the table grows.
        builder.HasIndex(j => new { j.State, j.AvailableAt }).HasDatabaseName("ix_crawl_jobs_state_available_at");
    }
}
