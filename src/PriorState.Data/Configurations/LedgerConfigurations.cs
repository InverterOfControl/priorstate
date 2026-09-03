using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PriorState.Domain.Entities;

namespace PriorState.Data.Configurations;

internal sealed class TimestampAnchorConfiguration : IEntityTypeConfiguration<TimestampAnchor>
{
    public void Configure(EntityTypeBuilder<TimestampAnchor> builder)
    {
        builder.ToTable("timestamp_anchors");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.CoversFromUtc).IsRequired();
        builder.Property(a => a.CoversUntilUtc).IsRequired();
        builder.Property(a => a.MerkleRoot).IsRequired();

        // The DER token, byte for byte as the authority returned it. Never re-encoded: a
        // round-trip through any parser risks changing bytes that the signature covers.
        builder.Property(a => a.TimestampToken).HasColumnType("bytea").IsRequired();

        builder.Property(a => a.TsaUrl).HasMaxLength(500).IsRequired();
        builder.Property(a => a.QualifiedProvider).IsRequired();

        // Deliberately not unique. One anchor per day was the original design and it created a
        // trap: a capture starting at 23:59 is dated to a day that may already be closed, leaving
        // it permanently unanchorable. Anchors cover sequence ranges, and a day may have several.
        builder.HasIndex(a => a.CoversFromUtc).HasDatabaseName("ix_timestamp_anchors_covers_from");
        builder.HasIndex(a => a.FirstChainSequence).HasDatabaseName("ix_timestamp_anchors_first_sequence");
    }
}

internal sealed class DeploymentLedgerEntryConfiguration : IEntityTypeConfiguration<DeploymentLedgerEntry>
{
    public void Configure(EntityTypeBuilder<DeploymentLedgerEntry> builder)
    {
        builder.ToTable("deployment_ledger_entries");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.CommitSha).HasMaxLength(64).IsRequired();
        builder.Property(d => d.CommitMessage).HasMaxLength(2000);
        builder.Property(d => d.Environment).HasMaxLength(100).IsRequired();
        builder.Property(d => d.Source).HasMaxLength(100).IsRequired();

        builder.HasOne(d => d.Project)
            .WithMany()
            .HasForeignKey(d => d.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(d => d.Run)
            .WithMany()
            .HasForeignKey(d => d.RunId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(d => new { d.ProjectId, d.DeployedAtUtc })
            .HasDatabaseName("ix_deployment_ledger_project_deployed_at");
        builder.HasIndex(d => d.CommitSha).HasDatabaseName("ix_deployment_ledger_commit_sha");
    }
}

internal sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable("audit_log");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Action).HasConversion<string>().HasMaxLength(64).IsRequired();
        builder.Property(a => a.SubjectType).HasMaxLength(64).IsRequired();
        builder.Property(a => a.SubjectId).HasMaxLength(64);
        builder.Property(a => a.UserId).HasMaxLength(450);
        builder.Property(a => a.UserName).HasMaxLength(256);
        builder.Property(a => a.Detail).HasMaxLength(4000);
        builder.Property(a => a.RemoteAddress).HasMaxLength(64);

        builder.HasIndex(a => a.OccurredAtUtc).HasDatabaseName("ix_audit_log_occurred_at");
        builder.HasIndex(a => new { a.SubjectType, a.SubjectId }).HasDatabaseName("ix_audit_log_subject");
    }
}
