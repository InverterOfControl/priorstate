using Microsoft.EntityFrameworkCore;
using PriorState.Api.Services;
using PriorState.Data;
using PriorState.Domain.Entities;
using PriorState.Evidence;
using PriorState.Ledger;

namespace PriorState.Api.Endpoints;

public static class ArchiveEndpoints
{
    public static void MapSnapshotEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/snapshots").RequireAuthorization().WithTags("Snapshots");

        // The timeline. Ordered by capture time, which is what a person reasons about, while the
        // chain sequence is what the proof reasons about; both are returned.
        group.MapGet("/", async (
            Guid? projectId,
            string? url,
            DateTimeOffset? from,
            DateTimeOffset? to,
            int take,
            PriorStateDbContext db,
            CancellationToken ct) =>
        {
            var query = db.Snapshots.AsNoTracking().Include(s => s.CaptureProfileVersion).AsQueryable();

            if (projectId is { } project)
            {
                query = query.Where(s => s.Run!.ProjectId == project);
            }

            if (!string.IsNullOrWhiteSpace(url))
            {
                query = query.Where(s => s.Url.Contains(url));
            }

            if (from is { } start)
            {
                query = query.Where(s => s.CapturedAtUtc >= start);
            }

            if (to is { } end)
            {
                query = query.Where(s => s.CapturedAtUtc <= end);
            }

            return await query
                .OrderByDescending(s => s.CapturedAtUtc)
                .Take(take is > 0 and <= 500 ? take : 100)
                .Select(s => new SnapshotSummary(
                    s.Id,
                    s.Url,
                    s.CapturedAtUtc,
                    s.ChainSequence,
                    s.EntryHash.Value,
                    s.CaptureProfileVersion!.Designation,
                    s.StorageWorm,
                    s.TimestampAnchorId != null))
                .ToListAsync(ct);
        });

        group.MapGet("/{id:guid}", async (
            Guid id,
            PriorStateDbContext db,
            AuditLog audit,
            CancellationToken ct) =>
        {
            var snapshot = await db.Snapshots
                .AsNoTracking()
                .Include(s => s.CaptureProfileVersion)
                .Include(s => s.TimestampAnchor)
                .FirstOrDefaultAsync(s => s.Id == id, ct);

            if (snapshot is null)
            {
                return Results.NotFound();
            }

            // Viewing is an access event and is logged as one.
            await audit.RecordAsync(AuditAction.SnapshotViewed, nameof(Snapshot), id.ToString(), snapshot.Url, ct);

            return Results.Ok(snapshot);
        });

        // Streams the WACZ for ReplayWeb.page. Range requests matter here: the replay component
        // seeks within the archive rather than downloading all of it.
        group.MapGet("/{id:guid}/archive", async (
            Guid id,
            PriorStateDbContext db,
            Storage.IObjectStore storage,
            AuditLog audit,
            CancellationToken ct) =>
        {
            var snapshot = await db.Snapshots.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
            if (snapshot is null)
            {
                return Results.NotFound();
            }

            await audit.RecordAsync(AuditAction.SnapshotReplayed, nameof(Snapshot), id.ToString(), snapshot.Url, ct);

            var stream = await storage.GetAsync(snapshot.WaczObjectKey, ct);
            return Results.Stream(stream, "application/wacz", $"{id}.wacz", enableRangeProcessing: true);
        });

        group.MapGet("/{id:guid}/evidence", async (
            Guid id,
            PriorStateDbContext db,
            EvidencePackageBuilder builder,
            AuditLog audit,
            CancellationToken ct) =>
        {
            var snapshot = await db.Snapshots
                .AsNoTracking()
                .Include(s => s.CaptureProfileVersion)
                .Include(s => s.TimestampAnchor)
                .FirstOrDefaultAsync(s => s.Id == id, ct);

            if (snapshot is null)
            {
                return Results.NotFound();
            }

            if (snapshot.TimestampAnchor is not { } anchor)
            {
                return Results.Problem(
                    "This snapshot has not been timestamped yet, so a package exported now could not be "
                    + "verified independently and none is produced. Scheduled anchoring covers complete "
                    + "days; to anchor everything pending right now, including today, POST to "
                    + "/api/ledger/anchor.",
                    statusCode: 409);
            }

            // The audit path needs the other entries under the same anchor, in chain order.
            var covered = await db.Snapshots
                .AsNoTracking()
                .Where(s => s.TimestampAnchorId == anchor.Id)
                .OrderBy(s => s.ChainSequence)
                .Select(s => new { s.Id, s.EntryHash })
                .ToListAsync(ct);

            var leafIndex = covered.FindIndex(e => e.Id == id);
            var hashes = covered.ConvertAll(e => e.EntryHash);

            var request = new EvidencePackageRequest
            {
                Snapshot = snapshot,
                Anchor = anchor,
                LeafIndex = leafIndex,
                AuditPath = MerkleTree.ComputeAuditPath(hashes, leafIndex),
            };

            await audit.RecordAsync(
                AuditAction.EvidencePackageExported, nameof(Snapshot), id.ToString(), snapshot.Url, ct);

            var buffer = new MemoryStream();
            await builder.BuildAsync(request, buffer, ct);
            buffer.Position = 0;

            return Results.File(
                buffer,
                "application/zip",
                $"priorstate-evidence-{snapshot.ChainSequence}-{id:n}.zip");
        });
    }

    public static void MapLedgerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ledger").RequireAuthorization().WithTags("Ledger");

        group.MapGet("/status", async (PriorStateDbContext db, Storage.IObjectStore storage, CancellationToken ct) =>
        {
            var tail = await db.Snapshots.AsNoTracking()
                .OrderByDescending(s => s.ChainSequence)
                .Select(s => new { s.ChainSequence, s.EntryHash, s.CapturedAtUtc })
                .FirstOrDefaultAsync(ct);

            var unanchored = await db.Snapshots.CountAsync(s => s.TimestampAnchorId == null, ct);
            var anchors = await db.TimestampAnchors.CountAsync(ct);
            var lastAnchor = await db.TimestampAnchors.AsNoTracking()
                .OrderByDescending(a => a.LastChainSequence)
                .FirstOrDefaultAsync(ct);

            return new LedgerStatus(
                tail?.ChainSequence ?? 0,
                tail?.EntryHash.Value,
                tail?.CapturedAtUtc,
                unanchored,
                anchors,
                lastAnchor?.TsaGeneralizedTime,
                lastAnchor?.QualifiedProvider ?? false,
                storage.WormCapability);
        });

        // Anchors everything pending, including entries captured minutes ago. The scheduled job
        // leaves the current day alone to keep timestamp-authority costs bounded; this is the
        // escape hatch for an operator who needs an evidence package today and has decided the
        // extra token is worth it. Same code path as the scheduled run.
        group.MapPost("/anchor", async (
            TimestampAnchorService anchors,
            AuditLog audit,
            CancellationToken ct) =>
        {
            var result = await anchors.AnchorPendingAsync(includeToday: true, ct);

            await audit.RecordAsync(
                AuditAction.TimestampAnchorCreated,
                "Ledger",
                result.AnchorId?.ToString(),
                result.DidAnchor
                    ? $"{result.EntriesAnchored} entries under root {result.MerkleRoot}"
                    : "nothing pending",
                ct);

            return Results.Ok(result);
        });

        // Re-derives the chain from scratch. Deliberately not sampled: a verification that checks
        // only some entries proves only that those entries are intact.
        group.MapPost("/verify", async (
            SnapshotLedger ledger,
            AuditLog audit,
            CancellationToken ct) =>
        {
            var result = await ledger.VerifyAsync(cancellationToken: ct);

            await audit.RecordAsync(
                AuditAction.ChainVerificationRun,
                "Ledger",
                detail: result.IsIntact
                    ? $"intact, {result.EntriesChecked} entries"
                    : $"FAILED at sequence {result.FailedChainSequence}: {result.Explanation}",
                cancellationToken: ct);

            return Results.Ok(result);
        });
    }
}

public sealed record SnapshotSummary(
    Guid Id,
    string Url,
    DateTimeOffset CapturedAtUtc,
    long ChainSequence,
    string EntryHash,
    string CaptureProfile,
    WormSupport StorageWorm,
    bool Timestamped);

public sealed record LedgerStatus(
    long ChainLength,
    string? HeadHash,
    DateTimeOffset? LastCapture,
    int SnapshotsAwaitingTimestamp,
    int TimestampAnchors,
    DateTimeOffset? LastAnchoredAt,
    bool LastAnchorQualified,
    WormSupport StorageWorm);
