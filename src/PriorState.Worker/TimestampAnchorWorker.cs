using Microsoft.EntityFrameworkCore;
using PriorState.Data;
using PriorState.Domain.Entities;
using PriorState.Ledger;
using PriorState.Ledger.Timestamping;

namespace PriorState.Worker;

/// <summary>
/// Once a day, computes the Merkle root over that day's ledger entries, has it timestamped by an
/// external authority, and links the entries to the resulting anchor.
///
/// This is the step that makes the archive provable to someone who does not trust the operator.
/// Until a day is anchored its entries are internally consistent but rest on nothing external;
/// after it, altering any of them contradicts a signature the operator cannot forge.
///
/// Anchoring is per-day rather than per-snapshot because timestamp tokens from a qualified
/// authority cost money per request. One token covers an entire day, and any individual entry can
/// still be proven against it with a short audit path.
/// </summary>
public sealed partial class TimestampAnchorWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITimestampAuthority _authority;
    private readonly ILogger<TimestampAnchorWorker> _logger;

    public TimestampAnchorWorker(
        IServiceScopeFactory scopeFactory,
        ITimestampAuthority authority,
        ILogger<TimestampAnchorWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _authority = authority;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Runs hourly rather than at a fixed time of day: an installation that is switched off
        // overnight would otherwise never anchor, and unanchored entries are the one backlog this
        // system must not accumulate.
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));

        do
        {
            try
            {
                await AnchorPendingDaysAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // A failing authority must not stop the loop; the day stays pending.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogAnchoringFailed(ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task AnchorPendingDaysAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PriorStateDbContext>();
        var ledger = scope.ServiceProvider.GetRequiredService<SnapshotLedger>();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Only complete days are anchored. Anchoring the current day would leave later entries
        // from the same day with no anchor and no obvious place to put them.
        var pendingDays = await db.Snapshots
            .AsNoTracking()
            .Where(s => s.TimestampAnchorId == null)
            .Select(s => DateOnly.FromDateTime(s.CapturedAtUtc.UtcDateTime))
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var day in pendingDays.Where(d => d < today).OrderBy(d => d))
        {
            await AnchorDayAsync(db, ledger, day, cancellationToken);
        }
    }

    private async Task AnchorDayAsync(
        PriorStateDbContext db,
        SnapshotLedger ledger,
        DateOnly day,
        CancellationToken cancellationToken)
    {
        var entries = await ledger.GetDayAsync(day, cancellationToken);
        if (entries.Count == 0)
        {
            return;
        }

        var hashes = entries.Select(e => e.EntryHash).ToList();
        var root = MerkleTree.ComputeRoot(hashes);

        var stamped = await _authority.TimestampAsync(root, cancellationToken);

        var anchor = new TimestampAnchor
        {
            CoversDateUtc = day,
            FirstChainSequence = entries[0].ChainSequence,
            LastChainSequence = entries[^1].ChainSequence,
            MerkleRoot = root,
            TimestampToken = stamped.Token,
            TsaUrl = stamped.TsaUrl,
            TsaGeneralizedTime = stamped.GeneralizedTime,
            QualifiedProvider = stamped.Qualified,
        };

        db.TimestampAnchors.Add(anchor);
        await db.SaveChangesAsync(cancellationToken);

        // Setting TimestampAnchorId is the single update the append-only trigger permits on the
        // snapshots table, and only while it is still null. Nothing that feeds the hash changes.
        await db.Snapshots
            .Where(s => s.TimestampAnchorId == null
                        && s.ChainSequence >= anchor.FirstChainSequence
                        && s.ChainSequence <= anchor.LastChainSequence)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.TimestampAnchorId, anchor.Id), cancellationToken);

        LogDayAnchored(day, entries.Count, root.Value, stamped.GeneralizedTime);

        if (!stamped.Qualified)
        {
            LogUnqualifiedAnchor(day);
        }
    }

    [LoggerMessage(
        EventId = 6100, Level = LogLevel.Information,
        Message = "Anchored {Day}: {EntryCount} entries under root {MerkleRoot}, attested at {AttestedTime}.")]
    private partial void LogDayAnchored(DateOnly day, int entryCount, string merkleRoot, DateTimeOffset attestedTime);

    [LoggerMessage(
        EventId = 6101, Level = LogLevel.Warning,
        Message = "The anchor for {Day} came from a timestamp authority that is not marked as qualified "
                  + "under eIDAS. It cannot be re-anchored later — configure a qualified provider before "
                  + "capturing anything intended for use in a dispute.")]
    private partial void LogUnqualifiedAnchor(DateOnly day);

    [LoggerMessage(EventId = 6102, Level = LogLevel.Error, Message = "Timestamp anchoring failed.")]
    private partial void LogAnchoringFailed(Exception exception);
}
