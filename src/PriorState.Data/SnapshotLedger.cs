using Microsoft.EntityFrameworkCore;
using PriorState.Domain.Entities;
using PriorState.Domain.ValueObjects;
using PriorState.Ledger;

namespace PriorState.Data;

/// <summary>
/// The only way a snapshot enters the ledger.
///
/// Appending is serialised with a Postgres advisory lock rather than optimistic retry. Two
/// concurrent appends racing for the same chain sequence would either produce a duplicate
/// (rejected by the unique index) or, worse, two entries both claiming the same predecessor. The
/// lock is held for the length of a single insert, so the contention cost is irrelevant next to
/// the cost of getting the chain wrong.
/// </summary>
public sealed class SnapshotLedger
{
    /// <summary>Arbitrary but fixed key identifying the chain-append lock within Postgres.</summary>
    private const long AdvisoryLockKey = 0x5052494F52535441; // "PRIORSTA"

    private readonly PriorStateDbContext _db;

    public SnapshotLedger(PriorStateDbContext db) => _db = db;

    /// <summary>
    /// Links <paramref name="snapshot"/> onto the tail of the chain and inserts it. The caller
    /// must have populated everything the canonical form reads, including a loaded
    /// CaptureProfileVersion — the hash is computed here and is final.
    /// </summary>
    public async Task<Snapshot> AppendAsync(Snapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // A caller that already opened a transaction owns it, and the advisory lock it holds
        // covers this work too.
        if (_db.Database.CurrentTransaction is not null)
        {
            return await AppendCoreAsync(snapshot, cancellationToken);
        }

        // The connection is configured with EnableRetryOnFailure, and EF refuses a user-initiated
        // transaction under a retrying strategy unless the whole unit runs inside that strategy —
        // otherwise a retry would replay half a transaction. Reading the chain tail, linking onto
        // it and inserting have to succeed or fail together, so they go in as one retriable unit.
        var strategy = _db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

            var appended = await AppendCoreAsync(snapshot, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return appended;
        });
    }

    /// <summary>
    /// The append itself, assuming an ambient transaction. The advisory lock is transaction-scoped
    /// and is released when that transaction ends, however it ends.
    /// </summary>
    private async Task<Snapshot> AppendCoreAsync(Snapshot snapshot, CancellationToken cancellationToken)
    {
        await _db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock({AdvisoryLockKey})",
            cancellationToken);

        var tail = await _db.Snapshots
            .AsNoTracking()
            .OrderByDescending(s => s.ChainSequence)
            .Select(s => new { s.ChainSequence, s.EntryHash })
            .FirstOrDefaultAsync(cancellationToken);

        // Recomputed on every attempt rather than reused: a retry may see a different tail, and
        // the entry must hash over the predecessor it actually follows.
        HashChain.Link(
            snapshot,
            previousSequence: tail?.ChainSequence ?? 0,
            previousHash: tail?.EntryHash ?? Sha256Hash.Genesis);

        _db.Snapshots.Add(snapshot);
        await _db.SaveChangesAsync(cancellationToken);

        return snapshot;
    }

    /// <summary>
    /// Re-derives the chain over a sequence range. Used by the scheduled integrity check, by the
    /// evidence export, and on demand from the UI. Reading the whole chain is intentional: a
    /// verification that only samples proves nothing.
    /// </summary>
    public async Task<ChainVerificationResult> VerifyAsync(
        long fromSequence = 1,
        long? toSequence = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Snapshots
            .AsNoTracking()
            .Include(s => s.CaptureProfileVersion)
            .Where(s => s.ChainSequence >= fromSequence);

        if (toSequence is { } upper)
        {
            query = query.Where(s => s.ChainSequence <= upper);
        }

        var snapshots = await query.OrderBy(s => s.ChainSequence).ToListAsync(cancellationToken);

        return HashChain.Verify(snapshots);
    }

    /// <summary>Entry hashes for one UTC day, in chain order, ready for the Merkle root.</summary>
    public async Task<IReadOnlyList<Snapshot>> GetDayAsync(
        DateOnly utcDay,
        CancellationToken cancellationToken = default)
    {
        var start = new DateTimeOffset(utcDay.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = start.AddDays(1);

        return await _db.Snapshots
            .AsNoTracking()
            .Where(s => s.CapturedAtUtc >= start && s.CapturedAtUtc < end)
            .OrderBy(s => s.ChainSequence)
            .ToListAsync(cancellationToken);
    }
}
