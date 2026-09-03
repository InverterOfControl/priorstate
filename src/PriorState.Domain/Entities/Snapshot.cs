using PriorState.Domain.ValueObjects;

namespace PriorState.Domain.Entities;

/// <summary>
/// One captured state of one URL, and one link in the hash chain.
///
/// This is the ledger. Rows are inserted and never modified: the database role the application
/// runs as has no UPDATE or DELETE grant on this table, and a trigger raises on either. See the
/// append-only migration in PriorState.Data.
/// </summary>
public sealed class Snapshot
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid RunId { get; set; }

    public Run? Run { get; set; }

    /// <summary>The URL as requested, before any redirect.</summary>
    public required string Url { get; set; }

    /// <summary>Where the browser ended up, if it differs. A redirect is itself evidence.</summary>
    public string? FinalUrl { get; set; }

    /// <summary>UTC, to the second. Local time never appears anywhere in the chain.</summary>
    public required DateTimeOffset CapturedAtUtc { get; set; }

    /// <summary>
    /// SHA-256 of the payload as stored. The anchor between the chain and the artefact.
    ///
    /// The payload is the WACZ archive for a page capture, and whatever a capture plugin returned
    /// for a plugin snapshot. Which one it is follows from CanonicalFormVersion; the hash means the
    /// same thing either way, so the chain does not need to care.
    /// </summary>
    public required Sha256Hash PayloadSha256 { get; set; }

    public required string PayloadObjectKey { get; set; }

    public required long PayloadSizeBytes { get; set; }

    /// <summary>The media type of the payload as stored, e.g. "application/wacz".</summary>
    public required string PayloadMediaType { get; set; }

    public Guid CaptureProfileVersionId { get; set; }

    public CaptureProfileVersion? CaptureProfileVersion { get; set; }

    /// <summary>
    /// The browser conditions the capture actually ran under.
    ///
    /// Null for a plugin snapshot: an API call has no viewport, no user agent and no Chromium
    /// version, and recording invented ones would be a false statement in the one document that
    /// has to survive being read adversarially. The v1 canonical form requires this and refuses
    /// to render without it.
    /// </summary>
    public CaptureConditions? Conditions { get; set; }

    // --- Provenance, for a snapshot produced by a capture plugin ---

    /// <summary>
    /// Which canonical form this entry's hash was computed over. Never changes after insert, and
    /// is not itself hashed — it selects the renderer rather than being one of its fields.
    ///
    /// Page captures are written as v1 and stay v1 forever; plugin snapshots are written as v2.
    /// </summary>
    public required string CanonicalFormVersion { get; set; }

    /// <summary>The binding that produced this snapshot, or null for a page capture.</summary>
    public Guid? PluginBindingVersionId { get; set; }

    public PluginBindingVersion? PluginBindingVersion { get; set; }

    /// <summary>
    /// The plugin's version as observed at execution time, read from the assembly that actually
    /// ran rather than from anything the plugin or the configuration declared about itself.
    /// </summary>
    public string? PluginVersion { get; set; }

    /// <summary>Extracted page text, for full-text search and diffing. Not part of the hash input.</summary>
    public string? ExtractedText { get; set; }

    // --- Hash chain ---

    /// <summary>Position in the chain. Contiguous from 1, with no gaps; a gap is tampering.</summary>
    public required long ChainSequence { get; set; }

    /// <summary>EntryHash of the preceding entry, or Sha256Hash.Genesis for the first.</summary>
    public required Sha256Hash PreviousHash { get; set; }

    /// <summary>SHA-256 over the canonical form. See PriorState.Ledger.CanonicalSnapshotForm.</summary>
    public required Sha256Hash EntryHash { get; set; }

    /// <summary>Set once the day's Merkle root has been timestamped. Null until then.</summary>
    public Guid? TimestampAnchorId { get; set; }

    public TimestampAnchor? TimestampAnchor { get; set; }

    // --- Storage immutability, as observed rather than as assumed ---

    /// <summary>
    /// Whether the object store actually enforced WORM for this object, probed at write time.
    /// Recorded per snapshot and printed in the evidence package: a claim of immutability that
    /// was never verified is worse than no claim at all.
    /// </summary>
    public required WormSupport StorageWorm { get; set; }

    /// <summary>Set when StorageWorm is Enforced. The date until which deletion is refused.</summary>
    public DateTimeOffset? WormRetainUntil { get; set; }
}

/// <summary>What the configured object store was observed to do, not what it advertises.</summary>
public enum WormSupport
{
    /// <summary>The backend has no object-lock API. Tamper-evidence rests on the chain alone.</summary>
    Unsupported = 0,

    /// <summary>The API accepted a retention setting, but enforcement was not verified.</summary>
    ApiPresentUnverified = 1,

    /// <summary>Retention was set and a probe delete was actually refused.</summary>
    Enforced = 2,
}
