using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PriorState.Domain.Entities;
using PriorState.Ledger;
using PriorState.Ledger.Timestamping;

namespace PriorState.Data;

/// <summary>
/// Anchors pending ledger entries to an external RFC-3161 timestamp.
///
/// Shared by the scheduled worker and the on-demand endpoint so both take exactly the same path —
/// an anchor produced by hand must be indistinguishable from one produced on schedule, or the
/// difference becomes something to argue about.
/// </summary>
public sealed partial class TimestampAnchorService
{
    private readonly PriorStateDbContext _db;
    private readonly ITimestampAuthority _authority;
    private readonly ILogger<TimestampAnchorService> _logger;

    public TimestampAnchorService(
        PriorStateDbContext db,
        ITimestampAuthority authority,
        ILogger<TimestampAnchorService> logger)
    {
        _db = db;
        _authority = authority;
        _logger = logger;
    }

    /// <summary>
    /// Anchors everything not yet anchored, oldest first, in a single Merkle tree.
    ///
    /// <paramref name="includeToday"/> is what separates the two callers. The scheduled job leaves
    /// the current UTC day alone: one token from a qualified authority costs money per request, and
    /// batching a whole day keeps that bounded. An operator asking for an anchor now has decided
    /// that the cost is worth it — usually because they need an evidence package today — and gets
    /// everything including entries captured minutes ago.
    /// </summary>
    public async Task<AnchorResult> AnchorPendingAsync(
        bool includeToday,
        CancellationToken cancellationToken = default)
    {
        var pending = await GetPendingAsync(includeToday, cancellationToken);

        if (pending.Count == 0)
        {
            return AnchorResult.NothingPending();
        }

        var hashes = pending.Select(s => s.EntryHash).ToList();
        var root = MerkleTree.ComputeRoot(hashes);

        // The authority is contacted before anything is written. A failure here leaves the entries
        // pending for the next attempt rather than recording an anchor with no token behind it.
        var stamped = await _authority.TimestampAsync(root, cancellationToken);

        var anchor = new TimestampAnchor
        {
            CoversFromUtc = pending[0].CapturedAtUtc,
            CoversUntilUtc = pending[^1].CapturedAtUtc,
            FirstChainSequence = pending[0].ChainSequence,
            LastChainSequence = pending[^1].ChainSequence,
            MerkleRoot = root,
            TimestampToken = stamped.Token,
            TsaUrl = stamped.TsaUrl,
            TsaGeneralizedTime = stamped.GeneralizedTime,
            QualifiedProvider = stamped.Qualified,
        };

        _db.TimestampAnchors.Add(anchor);
        await _db.SaveChangesAsync(cancellationToken);

        // Setting TimestampAnchorId is the single update the append-only trigger permits on the
        // snapshots table, and only while it is still null. Nothing that feeds a hash changes.
        // Scoped to the exact ids anchored, so an entry inserted between the read above and this
        // write is left pending rather than silently claimed by a root that does not cover it.
        var ids = pending.Select(s => s.Id).ToList();
        await _db.Snapshots
            .Where(s => ids.Contains(s.Id) && s.TimestampAnchorId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.TimestampAnchorId, anchor.Id), cancellationToken);

        LogAnchored(pending.Count, anchor.FirstChainSequence, anchor.LastChainSequence, root.Value,
            stamped.GeneralizedTime);

        if (!stamped.Qualified)
        {
            LogUnqualifiedAnchor();
        }

        return AnchorResult.Anchored(anchor, pending.Count);
    }

    private async Task<List<Snapshot>> GetPendingAsync(bool includeToday, CancellationToken cancellationToken)
    {
        var query = _db.Snapshots.AsNoTracking().Where(s => s.TimestampAnchorId == null);

        if (!includeToday)
        {
            var startOfToday = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
            query = query.Where(s => s.CapturedAtUtc < startOfToday);
        }

        return await query.OrderBy(s => s.ChainSequence).ToListAsync(cancellationToken);
    }

    [LoggerMessage(
        EventId = 6100, Level = LogLevel.Information,
        Message = "Anchored {EntryCount} entries (chain {First}–{Last}) under root {MerkleRoot}, "
                  + "attested at {AttestedTime}.")]
    private partial void LogAnchored(
        int entryCount, long first, long last, string merkleRoot, DateTimeOffset attestedTime);

    [LoggerMessage(
        EventId = 6101, Level = LogLevel.Warning,
        Message = "That anchor came from a timestamp authority not marked as qualified under eIDAS. It "
                  + "cannot be redone later — configure a qualified provider before capturing anything "
                  + "intended for use in a dispute.")]
    private partial void LogUnqualifiedAnchor();
}

public sealed record AnchorResult
{
    public required bool DidAnchor { get; init; }

    public int EntriesAnchored { get; init; }

    public Guid? AnchorId { get; init; }

    public string? MerkleRoot { get; init; }

    public DateTimeOffset? AttestedAt { get; init; }

    /// <summary>False when the authority is not a qualified eIDAS provider. Surfaced to the caller.</summary>
    public bool Qualified { get; init; }

    public static AnchorResult NothingPending() => new() { DidAnchor = false };

    public static AnchorResult Anchored(TimestampAnchor anchor, int count)
    {
        ArgumentNullException.ThrowIfNull(anchor);

        return new AnchorResult
        {
            DidAnchor = true,
            EntriesAnchored = count,
            AnchorId = anchor.Id,
            MerkleRoot = anchor.MerkleRoot.Value,
            AttestedAt = anchor.TsaGeneralizedTime,
            Qualified = anchor.QualifiedProvider,
        };
    }
}
