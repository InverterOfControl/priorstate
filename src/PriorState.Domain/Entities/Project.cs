using PriorState.Domain.ValueObjects;

namespace PriorState.Domain.Entities;

/// <summary>A site being archived, and the schedule and retention it is archived under.</summary>
public sealed class Project
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    /// <summary>Seed URLs for the crawl.</summary>
    public List<string> SeedUrls { get; set; } = [];

    /// <summary>URL prefixes the crawl may follow. Empty means "the seed hosts only".</summary>
    public List<string> ScopeIncludes { get; set; } = [];

    public List<string> ScopeExcludes { get; set; } = [];

    /// <summary>Cron expression for scheduled captures. Null means triggered captures only.</summary>
    public string? Schedule { get; set; }

    /// <summary>
    /// How long snapshots are kept. Can be extended but never shortened — shortening retention
    /// after the fact would let an operator make inconvenient snapshots disappear on a timer.
    /// </summary>
    public required int RetentionYears { get; set; }

    public Guid CaptureProfileVersionId { get; set; }

    public CaptureProfileVersion? CaptureProfileVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool Archived { get; set; }
}
