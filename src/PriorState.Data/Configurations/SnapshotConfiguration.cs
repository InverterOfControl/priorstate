using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PriorState.Domain.Entities;

namespace PriorState.Data.Configurations;

internal sealed class SnapshotConfiguration : IEntityTypeConfiguration<Snapshot>
{
    public void Configure(EntityTypeBuilder<Snapshot> builder)
    {
        builder.ToTable("snapshots");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Url).HasMaxLength(2048).IsRequired();
        builder.Property(s => s.FinalUrl).HasMaxLength(2048);
        builder.Property(s => s.PayloadObjectKey).HasMaxLength(1024).IsRequired();
        builder.Property(s => s.PayloadMediaType).HasMaxLength(200).IsRequired();
        builder.Property(s => s.CanonicalFormVersion).HasMaxLength(64).IsRequired();
        builder.Property(s => s.PluginVersion).HasMaxLength(120);
        builder.Property(s => s.ExtractedText);

        // Contiguous and unique. A duplicate is a bug; a gap is tampering. The database refuses
        // the former outright so the latter is the only failure mode verification has to reason
        // about.
        builder.Property(s => s.ChainSequence).IsRequired();
        builder.HasIndex(s => s.ChainSequence).IsUnique().HasDatabaseName("ix_snapshots_chain_sequence");

        builder.Property(s => s.PreviousHash).IsRequired();
        builder.Property(s => s.EntryHash).IsRequired();
        builder.HasIndex(s => s.EntryHash).IsUnique().HasDatabaseName("ix_snapshots_entry_hash");

        builder.Property(s => s.PayloadSha256).IsRequired();
        builder.HasIndex(s => s.PayloadSha256).HasDatabaseName("ix_snapshots_payload_sha256");

        builder.Property(s => s.StorageWorm).HasConversion<string>().HasMaxLength(32).IsRequired();

        // The conditions the capture actually ran under, flattened into the row. Stored per
        // snapshot rather than only referenced through the profile, because the profile is what
        // was asked for and this is what happened.
        //
        // Optional since plugin snapshots exist: an API call has no viewport or browser version,
        // and the v2 canonical form omits these fields rather than inventing values for them. The
        // v1 renderer refuses to hash a page capture that is missing them.
        builder.ComplexProperty(s => s.Conditions, conditions =>
        {
            conditions.IsRequired(false);
            conditions.Property(c => c.UserAgent).HasMaxLength(512);
            conditions.Property(c => c.CookieBanner).HasConversion<string>().HasMaxLength(32);
            conditions.Property(c => c.ChromiumVersion).HasMaxLength(64);
            conditions.Property(c => c.CrawlerVersion).HasMaxLength(64);
        });

        builder.HasOne(s => s.Run)
            .WithMany(r => r.Snapshots)
            .HasForeignKey(s => s.RunId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.CaptureProfileVersion)
            .WithMany()
            .HasForeignKey(s => s.CaptureProfileVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.PluginBindingVersion)
            .WithMany()
            .HasForeignKey(s => s.PluginBindingVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.TimestampAnchor)
            .WithMany()
            .HasForeignKey(s => s.TimestampAnchorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => new { s.Url, s.CapturedAtUtc }).HasDatabaseName("ix_snapshots_url_captured_at");
    }
}
