using PriorState.Domain.ValueObjects;
using PriorState.Ledger;

namespace PriorState.Ledger.Tests;

public sealed class HashChainTests
{
    [Fact]
    public void Verify_AcceptsAnIntactChain()
    {
        var result = HashChain.Verify(LedgerTestData.Chain(25));

        Assert.True(result.IsIntact);
        Assert.Equal(25, result.EntriesChecked);
        Assert.Null(result.Defect);
    }

    [Fact]
    public void Verify_AcceptsAnEmptyRange()
    {
        Assert.True(HashChain.Verify([]).IsIntact);
    }

    [Fact]
    public void Verify_DetectsAnAlteredField()
    {
        var chain = LedgerTestData.Chain(10);

        // Someone edits a recorded URL in the database but leaves the stored hash alone.
        chain[4].Url = "https://example.com/page-innocuous";

        var result = HashChain.Verify(chain);

        Assert.False(result.IsIntact);
        Assert.Equal(ChainDefect.EntryHashMismatch, result.Defect);
        Assert.Equal(5, result.FailedChainSequence);
        Assert.Equal(chain[4].Id, result.FailedSnapshotId);
    }

    [Fact]
    public void Verify_DetectsARemovedEntry()
    {
        var chain = LedgerTestData.Chain(10);
        chain.RemoveAt(6);

        var result = HashChain.Verify(chain);

        Assert.False(result.IsIntact);
        Assert.Equal(ChainDefect.SequenceGap, result.Defect);
        Assert.Equal(8, result.FailedChainSequence);
    }

    [Fact]
    public void Verify_DetectsAReplacedEntryThatWasRehashed()
    {
        var chain = LedgerTestData.Chain(10);

        // The most sophisticated attempt: rewrite the entry AND recompute its own hash, so the
        // entry is internally consistent. It still cannot match what its successor points at.
        chain[3].Url = "https://example.com/substituted";
        chain[3].EntryHash = CanonicalSnapshotForm.ComputeEntryHash(chain[3]);

        var result = HashChain.Verify(chain);

        Assert.False(result.IsIntact);
        Assert.Equal(ChainDefect.BrokenLink, result.Defect);
        Assert.Equal(5, result.FailedChainSequence);
    }

    [Fact]
    public void Verify_DetectsReorderedEntries()
    {
        var chain = LedgerTestData.Chain(10);
        (chain[2], chain[7]) = (chain[7], chain[2]);

        Assert.False(HashChain.Verify(chain).IsIntact);
    }

    [Fact]
    public void Link_ChainsOntoThePreviousEntry()
    {
        var first = LedgerTestData.Snapshot(sequence: 1);
        var second = LedgerTestData.Snapshot(url: "https://example.com/second");

        HashChain.Link(second, previousSequence: first.ChainSequence, previousHash: first.EntryHash);

        Assert.Equal(2, second.ChainSequence);
        Assert.Equal(first.EntryHash, second.PreviousHash);
        Assert.Equal(CanonicalSnapshotForm.ComputeEntryHash(second), second.EntryHash);
        Assert.True(HashChain.Verify([first, second]).IsIntact);
    }

    [Fact]
    public void Link_StartsFromGenesisForTheFirstEntry()
    {
        var first = LedgerTestData.Snapshot();

        HashChain.Link(first, previousSequence: 0, previousHash: Sha256Hash.Genesis);

        Assert.Equal(1, first.ChainSequence);
        Assert.Equal(Sha256Hash.Genesis, first.PreviousHash);
    }
}
