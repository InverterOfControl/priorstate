using PriorState.Domain.Entities;

namespace PriorState.Ledger;

/// <summary>Outcome of re-deriving a stretch of the hash chain.</summary>
public sealed record ChainVerificationResult
{
    private ChainVerificationResult()
    {
    }

    public bool IsIntact => Defect is null;

    public int EntriesChecked { get; private init; }

    public ChainDefect? Defect { get; private init; }

    /// <summary>The first entry that failed. Null when the chain is intact.</summary>
    public Guid? FailedSnapshotId { get; private init; }

    public long? FailedChainSequence { get; private init; }

    /// <summary>Plain-language explanation, suitable for the audit log and the UI.</summary>
    public string? Explanation { get; private init; }

    public static ChainVerificationResult Intact(int entriesChecked) => new()
    {
        EntriesChecked = entriesChecked,
    };

    public static ChainVerificationResult Broken(Snapshot snapshot, ChainDefect defect, string explanation)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new ChainVerificationResult
        {
            Defect = defect,
            FailedSnapshotId = snapshot.Id,
            FailedChainSequence = snapshot.ChainSequence,
            Explanation = explanation,
        };
    }
}

public enum ChainDefect
{
    /// <summary>A recorded field was altered after the fact.</summary>
    EntryHashMismatch = 0,

    /// <summary>An entry does not follow from its predecessor.</summary>
    BrokenLink = 1,

    /// <summary>An entry is missing from the sequence.</summary>
    SequenceGap = 2,
}
