using Microsoft.EntityFrameworkCore;
using PriorState.Data;
using PriorState.Domain.Entities;
using PriorState.Domain.ValueObjects;
using PriorState.Ledger;

namespace PriorState.Data.Tests;

[Collection(nameof(PostgresCollection))]
public sealed class SnapshotLedgerTests
{
    private readonly PostgresFixture _postgres;

    public SnapshotLedgerTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task Append_LinksEachEntryToItsPredecessor()
    {
        await using var db = _postgres.CreateContext();

        var first = await PostgresFixture.SeedSnapshotAsync(db, "https://example.com/one");
        var second = await PostgresFixture.SeedSnapshotAsync(db, "https://example.com/two");

        Assert.Equal(first.ChainSequence + 1, second.ChainSequence);
        Assert.Equal(first.EntryHash, second.PreviousHash);
        Assert.NotEqual(Sha256Hash.Genesis, second.EntryHash);
    }

    [Fact]
    public async Task Append_ComputesTheEntryHashFromTheCanonicalForm()
    {
        await using var db = _postgres.CreateContext();
        var snapshot = await PostgresFixture.SeedSnapshotAsync(db);

        Assert.Equal(CanonicalSnapshotForm.ComputeEntryHash(snapshot), snapshot.EntryHash);
    }

    [Fact]
    public async Task Verify_ReportsTheStoredChainAsIntact()
    {
        await using var db = _postgres.CreateContext();
        await PostgresFixture.SeedSnapshotAsync(db);

        var ledger = new SnapshotLedger(db);
        var result = await ledger.VerifyAsync();

        Assert.True(result.IsIntact, result.Explanation);
    }

    [Fact]
    public async Task Append_UnderConcurrencyProducesAContiguousChain()
    {
        // The advisory lock is the whole point of this test: without it two appends can read the
        // same tail and both claim the same predecessor, which would silently fork the chain.
        const int Concurrency = 8;

        await using (var seed = _postgres.CreateContext())
        {
            await PostgresFixture.SeedSnapshotAsync(seed, "https://example.com/serialised-base");
        }

        var appends = Enumerable.Range(0, Concurrency).Select(async i =>
        {
            await using var db = _postgres.CreateContext();
            return await PostgresFixture.SeedSnapshotAsync(db, $"https://example.com/concurrent-{i}");
        });

        var appended = await Task.WhenAll(appends);
        var sequences = appended.Select(s => s.ChainSequence).OrderBy(s => s).ToList();

        Assert.Equal(Concurrency, sequences.Distinct().Count());
        Assert.Equal(sequences.Count, sequences[^1] - sequences[0] + 1);

        await using var verifyDb = _postgres.CreateContext();
        var result = await new SnapshotLedger(verifyDb).VerifyAsync();
        Assert.True(result.IsIntact, result.Explanation);
    }

    [Fact]
    public async Task GetUnanchored_ReturnsPendingEntriesInChainOrder()
    {
        await using var db = _postgres.CreateContext();
        var snapshot = await PostgresFixture.SeedSnapshotAsync(db, "https://example.com/pending");

        var pending = await new SnapshotLedger(db).GetUnanchoredAsync();

        Assert.Contains(pending, s => s.Id == snapshot.Id);
        Assert.All(pending, s => Assert.Null(s.TimestampAnchorId));
        Assert.Equal(pending.OrderBy(s => s.ChainSequence).Select(s => s.Id), pending.Select(s => s.Id));
    }

    [Fact]
    public async Task ChainSequence_IsUniqueAtTheDatabaseLevel()
    {
        await using var db = _postgres.CreateContext();
        var existing = await PostgresFixture.SeedSnapshotAsync(db);

        // Belt to the advisory lock's braces: even a code path that bypassed SnapshotLedger
        // entirely cannot produce two entries at the same position.
        await Assert.ThrowsAnyAsync<DbUpdateException>(async () =>
        {
            await using var second = _postgres.CreateContext();
            var run = await second.Runs.FirstAsync(r => r.Id == existing.RunId);
            var profile = await second.CaptureProfileVersions.FirstAsync();

            second.Snapshots.Add(new Snapshot
            {
                RunId = run.Id,
                Url = "https://example.com/duplicate-sequence",
                CapturedAtUtc = DateTimeOffset.UtcNow,
                PayloadSha256 = Sha256Hash.Parse(new string('f', 64)),
                PayloadObjectKey = "test/duplicate.wacz",
                PayloadSizeBytes = 1,
                PayloadMediaType = "application/wacz",
                CanonicalFormVersion = CanonicalSnapshotForm.Version1,
                CaptureProfileVersionId = profile.Id,
                Conditions = PostgresFixture.TestConditions,
                ChainSequence = existing.ChainSequence,
                PreviousHash = existing.PreviousHash,
                EntryHash = Sha256Hash.Parse(new string('e', 64)),
                StorageWorm = WormSupport.Unsupported,
            });

            await second.SaveChangesAsync();
        });
    }
    [Fact]
    public async Task Verify_CoversPluginSnapshotsAsWellAsPageCaptures()
    {
        // Regression guard. Verification re-renders the canonical form of every entry, and the v2
        // renderer refuses to hash a snapshot whose binding is not loaded. When VerifyAsync did
        // not Include it, the first plugin snapshot in the chain made the whole ledger
        // unverifiable — a failure that only appears once a plugin has actually run.
        await using var db = _postgres.CreateContext();
        await PostgresFixture.SeedSnapshotAsync(db);
        await PostgresFixture.SeedPluginSnapshotAsync(db);

        await using var verifyDb = _postgres.CreateContext();
        var result = await new SnapshotLedger(verifyDb).VerifyAsync();

        Assert.True(result.IsIntact, result.Explanation);
    }

    [Fact]
    public async Task PluginSnapshot_LinksIntoTheSameChainAsPageCaptures()
    {
        await using var db = _postgres.CreateContext();
        var page = await PostgresFixture.SeedSnapshotAsync(db);
        var plugin = await PostgresFixture.SeedPluginSnapshotAsync(db);

        // One chain, not two. This is what makes a plugin entry share the daily Merkle root and
        // the timestamp that covers it.
        Assert.Equal(page.ChainSequence + 1, plugin.ChainSequence);
        Assert.Equal(page.EntryHash, plugin.PreviousHash);
    }
}
