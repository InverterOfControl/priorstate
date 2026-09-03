namespace PriorState.Domain.Entities;

/// <summary>
/// The bridge between "which code was deployed" and "what a visitor saw" — the gap that makes a
/// Git history insufficient on its own.
/// </summary>
public sealed class DeploymentLedgerEntry
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid ProjectId { get; set; }

    public Project? Project { get; set; }

    public required string CommitSha { get; set; }

    public string? CommitMessage { get; set; }

    public required string Environment { get; set; }

    public required DateTimeOffset DeployedAtUtc { get; set; }

    /// <summary>The run triggered by this deployment, once it has completed.</summary>
    public Guid? RunId { get; set; }

    public Run? Run { get; set; }

    /// <summary>Which system reported the deployment, e.g. "github-actions".</summary>
    public required string Source { get; set; }

    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;
}
