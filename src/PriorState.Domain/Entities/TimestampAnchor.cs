using PriorState.Domain.ValueObjects;

namespace PriorState.Domain.Entities;

/// <summary>
/// A Merkle root over a contiguous run of ledger entries, timestamped by an RFC-3161 authority.
///
/// This is what makes the archive provable to someone who does not trust the operator. The chain
/// alone shows internal consistency; the token shows that this exact chain state existed before a
/// moment attested by a third party. It survives the database and the bucket being wiped.
///
/// An anchor covers a range of chain sequences, not a calendar day. Scheduled anchoring still runs
/// once a day — one token from a qualified authority costs money, and batching a whole day keeps
/// that bounded — but nothing in the model depends on that. Several anchors may cover the same
/// day, which is what makes on-demand anchoring possible and what stops a capture that straddles
/// midnight from landing in a day that has already been closed.
/// </summary>
public sealed class TimestampAnchor
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Capture time of the earliest entry covered.</summary>
    public required DateTimeOffset CoversFromUtc { get; set; }

    /// <summary>Capture time of the latest entry covered.</summary>
    public required DateTimeOffset CoversUntilUtc { get; set; }

    public required long FirstChainSequence { get; set; }

    public required long LastChainSequence { get; set; }

    /// <summary>Merkle root over the EntryHash values in the range, in sequence order.</summary>
    public required Sha256Hash MerkleRoot { get; set; }

    /// <summary>The DER-encoded RFC-3161 TimeStampToken, stored verbatim.</summary>
    public required byte[] TimestampToken { get; set; }

    /// <summary>The TSA endpoint used. Part of the evidence package.</summary>
    public required string TsaUrl { get; set; }

    /// <summary>Time asserted by the authority, extracted from the token for display and querying.</summary>
    public required DateTimeOffset TsaGeneralizedTime { get; set; }

    /// <summary>
    /// False when the configured TSA is not a qualified eIDAS provider — FreeTSA, for instance.
    /// Surfaced loudly in the UI and printed on the protocol, because a snapshot anchored to a
    /// free demo service will not carry the day in a dispute.
    /// </summary>
    public required bool QualifiedProvider { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
