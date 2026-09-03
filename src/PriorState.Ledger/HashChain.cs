using PriorState.Domain.Entities;
using PriorState.Domain.ValueObjects;

namespace PriorState.Ledger;

/// <summary>
/// Building and checking the chain. Kept free of database and storage concerns so it can be
/// tested exhaustively and read by someone auditing the guarantee.
/// </summary>
public static class HashChain
{
    /// <summary>
    /// Fills in the chain fields for a snapshot about to be appended. The caller is responsible
    /// for doing this inside the same serialized transaction that reads the tail, so two
    /// concurrent appends cannot claim the same sequence number.
    /// </summary>
    public static void Link(Snapshot snapshot, long previousSequence, Sha256Hash previousHash)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        snapshot.ChainSequence = previousSequence + 1;
        snapshot.PreviousHash = previousHash;
        snapshot.EntryHash = CanonicalSnapshotForm.ComputeEntryHash(snapshot);
    }

    /// <summary>
    /// Re-derives every hash and link over a contiguous range of the chain, in ascending sequence
    /// order, and reports the first thing that does not add up.
    ///
    /// Three independent failures are detected: a recomputed entry hash that differs from the
    /// stored one (a field was altered), a previous-hash that does not match its predecessor (an
    /// entry was swapped), and a gap in the sequence (an entry was removed).
    /// </summary>
    public static ChainVerificationResult Verify(IReadOnlyList<Snapshot> orderedSnapshots)
    {
        ArgumentNullException.ThrowIfNull(orderedSnapshots);

        if (orderedSnapshots.Count == 0)
        {
            return ChainVerificationResult.Intact(0);
        }

        var expectedSequence = orderedSnapshots[0].ChainSequence;
        var expectedPrevious = orderedSnapshots[0].PreviousHash;

        foreach (var snapshot in orderedSnapshots)
        {
            if (snapshot.ChainSequence != expectedSequence)
            {
                return ChainVerificationResult.Broken(
                    snapshot,
                    ChainDefect.SequenceGap,
                    $"Expected chain sequence {expectedSequence}, found {snapshot.ChainSequence}. "
                    + "An entry has been removed or reordered.");
            }

            if (snapshot.PreviousHash != expectedPrevious)
            {
                return ChainVerificationResult.Broken(
                    snapshot,
                    ChainDefect.BrokenLink,
                    $"Entry {snapshot.ChainSequence} points at {snapshot.PreviousHash}, "
                    + $"but its predecessor hashes to {expectedPrevious}.");
            }

            var recomputed = CanonicalSnapshotForm.ComputeEntryHash(snapshot);
            if (recomputed != snapshot.EntryHash)
            {
                return ChainVerificationResult.Broken(
                    snapshot,
                    ChainDefect.EntryHashMismatch,
                    $"Entry {snapshot.ChainSequence} is recorded as {snapshot.EntryHash} but its "
                    + $"contents hash to {recomputed}. A recorded field has been altered.");
            }

            expectedSequence = snapshot.ChainSequence + 1;
            expectedPrevious = snapshot.EntryHash;
        }

        return ChainVerificationResult.Intact(orderedSnapshots.Count);
    }
}
