using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PriorState.Domain.Entities;

namespace PriorState.Data;

public sealed class PriorStateDbContext : IdentityDbContext<ApplicationUser>
{
    public PriorStateDbContext(DbContextOptions<PriorStateDbContext> options) : base(options)
    {
    }

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<CaptureProfileVersion> CaptureProfileVersions => Set<CaptureProfileVersion>();

    public DbSet<Run> Runs => Set<Run>();

    /// <summary>
    /// The ledger. Inserts only — see the append-only migration, which revokes UPDATE and DELETE
    /// from the application role and installs a trigger that raises on either. Nothing in this
    /// codebase modifies a snapshot, and the database will not let a future change do so by
    /// accident either.
    /// </summary>
    public DbSet<Snapshot> Snapshots => Set<Snapshot>();

    public DbSet<TimestampAnchor> TimestampAnchors => Set<TimestampAnchor>();

    public DbSet<DeploymentLedgerEntry> DeploymentLedgerEntries => Set<DeploymentLedgerEntry>();

    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    /// <summary>Operational state, not recorded history. This one is mutable on purpose.</summary>
    public DbSet<CrawlJob> CrawlJobs => Set<CrawlJob>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(PriorStateDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // Every hash in the model is stored the same way: 64 lowercase hex characters. Fixing it
        // once here keeps the storage form identical to the form that appears in the canonical
        // representation and in verify.sh.
        configurationBuilder.Properties<Domain.ValueObjects.Sha256Hash>()
            .HaveConversion<Sha256HashConverter>()
            .HaveMaxLength(Domain.ValueObjects.Sha256Hash.HexLength)
            .AreFixedLength();

        configurationBuilder.Properties<DateTimeOffset>().HaveColumnType("timestamptz");
    }
}
