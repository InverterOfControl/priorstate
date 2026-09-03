namespace PriorState.Domain.Entities;

/// <summary>
/// The work queue. Backed by Postgres and claimed with FOR UPDATE SKIP LOCKED, so the queue and
/// the ledger write live in one transaction and there is no broker in the compose file.
///
/// Unlike the ledger, this table is mutable — it is operational state, not recorded history.
/// </summary>
public sealed class CrawlJob
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid RunId { get; set; }

    public Run? Run { get; set; }

    public CrawlJobState State { get; set; } = CrawlJobState.Pending;

    public DateTimeOffset AvailableAt { get; set; } = DateTimeOffset.UtcNow;

    public int Attempts { get; set; }

    public int MaxAttempts { get; set; } = 3;

    /// <summary>Worker instance currently holding the claim, for visibility during incidents.</summary>
    public string? ClaimedBy { get; set; }

    public DateTimeOffset? ClaimedAt { get; set; }

    public string? LastError { get; set; }
}

public enum CrawlJobState
{
    Pending = 0,
    Claimed = 1,
    Completed = 2,
    Failed = 3,
}
