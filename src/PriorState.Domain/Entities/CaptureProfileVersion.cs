using PriorState.Domain.ValueObjects;

namespace PriorState.Domain.Entities;

/// <summary>
/// A named, versioned set of capture settings, e.g. "DE-Standard" v1.
///
/// These are rows, not sliders. Freely adjustable viewport, user agent or wait times would hand
/// the opposing side the argument that the capture was configured to produce a desired result.
/// A change creates a new version; it applies only to captures made after it, and existing
/// snapshots keep the version they were captured under. Rows are never updated or deleted.
/// </summary>
public sealed class CaptureProfileVersion
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Stable name across versions, e.g. "DE-Standard".</summary>
    public required string Name { get; set; }

    public required int Version { get; set; }

    public required CaptureConditions Conditions { get; set; }

    /// <summary>Why this version exists. Shown in the evidence package.</summary>
    public required string Rationale { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Set when a newer version exists. Does not invalidate snapshots taken under it.</summary>
    public DateTimeOffset? SupersededAt { get; set; }

    public string Designation => $"{Name} v{Version}";
}
