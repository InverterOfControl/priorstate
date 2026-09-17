using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PriorState.Domain.Entities;

namespace PriorState.Data.Configurations;

internal sealed class SourceExecutionConfiguration : IEntityTypeConfiguration<SourceExecution>
{
    public void Configure(EntityTypeBuilder<SourceExecution> builder)
    {
        builder.ToTable("source_executions");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.State).HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.Error).HasMaxLength(2000);
        builder.Property(e => e.MediaType).HasMaxLength(256);
        builder.HasOne(e => e.Binding).WithMany().HasForeignKey(e => e.BindingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.Run).WithMany(r => r.SourceExecutions).HasForeignKey(e => e.RunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => new { e.State, e.QueuedAt });
        builder.HasIndex(e => new { e.RunId, e.BindingId }).IsUnique().HasFilter("\"RunId\" IS NOT NULL");
        builder.HasIndex(e => e.BindingId).IsUnique()
            .HasFilter("\"RunId\" IS NULL AND \"State\" IN ('Queued', 'Running')")
            .HasDatabaseName("ix_source_executions_pending_test");
    }
}
