namespace PriorState.Domain.Entities;

/// <summary>One execution of a crawl for a project. Produces zero or more snapshots.</summary>
public sealed class Run
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid ProjectId { get; set; }

    public Project? Project { get; set; }

    /// <summary>The profile version in force when the run started. Frozen for the run's lifetime.</summary>
    public Guid CaptureProfileVersionId { get; set; }

    public CaptureProfileVersion? CaptureProfileVersion { get; set; }

    public required RunTrigger Trigger { get; set; }

    public RunStatus Status { get; set; } = RunStatus.Queued;

    public DateTimeOffset QueuedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>Exit code of the browsertrix-crawler container, once it has run.</summary>
    public int? CrawlerExitCode { get; set; }

    public string? FailureReason { get; set; }

    /// <summary>
    /// The exact container arguments used, recorded verbatim so a third party can reproduce the
    /// capture by hand.
    /// </summary>
    public List<string> CrawlerArguments { get; set; } = [];

    public List<Snapshot> Snapshots { get; set; } = [];
}

public enum RunTrigger
{
    Manual = 0,
    Scheduled = 1,
    Deployment = 2,
}

public enum RunStatus
{
    Queued = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
}
