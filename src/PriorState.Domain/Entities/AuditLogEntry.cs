namespace PriorState.Domain.Entities;

/// <summary>
/// Append-only record of who did and saw what. The process documentation asserts that access is
/// logged and that no manual intervention in the archive is possible; this table is the evidence
/// for the first half of that claim. Reads of snapshots are logged, not just writes.
/// </summary>
public sealed class AuditLogEntry
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required DateTimeOffset OccurredAtUtc { get; set; }

    /// <summary>Null for actions taken by the scheduler rather than a person.</summary>
    public string? UserId { get; set; }

    public string? UserName { get; set; }

    public required AuditAction Action { get; set; }

    /// <summary>Type of the thing acted on, e.g. "Snapshot".</summary>
    public required string SubjectType { get; set; }

    public string? SubjectId { get; set; }

    /// <summary>Free-form context. Never contains credentials.</summary>
    public string? Detail { get; set; }

    public string? RemoteAddress { get; set; }
}

public enum AuditAction
{
    SnapshotViewed = 0,
    SnapshotReplayed = 1,
    EvidencePackageExported = 2,
    RunTriggered = 3,
    ProjectCreated = 4,
    ProjectUpdated = 5,
    CaptureProfileVersionCreated = 6,
    RetentionExtended = 7,
    UserSignedIn = 8,
    UserSignInFailed = 9,
    ChainVerificationRun = 10,
}
