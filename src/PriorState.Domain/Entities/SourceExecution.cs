namespace PriorState.Domain.Entities;

/// <summary>Operational result of a source capture or a connection test; never part of the immutable ledger.</summary>
public sealed class SourceExecution
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BindingId { get; set; }
    public PluginBindingVersion? Binding { get; set; }
    /// <summary>Null for a connection test, which never writes a snapshot.</summary>
    public Guid? RunId { get; set; }
    public Run? Run { get; set; }
    public SourceExecutionState State { get; set; } = SourceExecutionState.Queued;
    public DateTimeOffset QueuedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public Guid? SnapshotId { get; set; }
    public long? SizeBytes { get; set; }
    public string? MediaType { get; set; }
    public string? Error { get; set; }
}

public enum SourceExecutionState
{
    Queued = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
}
