using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using PriorState.Api.Services;
using PriorState.Data;
using PriorState.Domain.Entities;
using PriorState.Domain.ValueObjects;
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
                    s.TimestampAnchorId != null,
                    s.PluginBindingVersion != null ? s.PluginBindingVersion.PluginId : null))
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
                .Include(s => s.PluginBindingVersion)
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

        // Streams the stored payload. For a plugin snapshot that is whatever the endpoint
        // returned, served under the media type that was recorded — handing a caller JSON
        // labelled application/wacz would be a lie about the one thing this file is supposed to
        // be authoritative about.
        //
        // For a page capture it is the WACZ, and a replay viewer does not download it: it reads
        // the ZIP directory at the end of the file, then the index, then individual records. Both
        // the length and real range support are therefore load-bearing, and neither can be left
        // to the framework, because the object store hands back a network stream that cannot
        // seek. ASP.NET quietly drops range processing for such a stream and answers with the
        // whole object under no Content-Length, which a viewer reports as being unable to get at
        // the size of the file. The length comes from the ledger entry instead, where it was
        // recorded at capture time as part of what was hashed, and the range is passed through to
        // the backend rather than served by discarding most of a full read.
        //
        // HEAD is mapped alongside GET deliberately. Without it the SPA fallback answers a size
        // probe with index.html, and a 200 carrying the wrong length is worse than a 404.
        group.MapMethods("/{id:guid}/archive", [HttpMethods.Get, HttpMethods.Head], async (
            Guid id,
            HttpContext http,
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

            var isPageCapture = string.Equals(
                snapshot.CanonicalFormVersion, CanonicalSnapshotForm.Version1, StringComparison.Ordinal);

            var fileName = isPageCapture
                ? $"{id}.wacz"
                : PayloadNaming.FileNameFor(snapshot.PayloadMediaType);

            var total = snapshot.PayloadSizeBytes;
            var response = http.Response;
            response.ContentType = snapshot.PayloadMediaType;
            response.Headers.AcceptRanges = "bytes";
            response.Headers.ContentDisposition =
                new ContentDispositionHeaderValue("attachment") { FileName = fileName }.ToString();

            // A size probe reads none of the archived bytes, so it is answered but not recorded
            // as a read of them.
            if (HttpMethods.IsHead(http.Request.Method))
            {
                response.ContentLength = total;
                return Results.Empty;
            }

            var outcome = ResolveRange(http.Request, total, out var from, out var to);

            if (outcome == RangeOutcome.Unsatisfiable)
            {
                response.Headers.ContentRange = $"bytes */{total}";
                return Results.StatusCode(StatusCodes.Status416RangeNotSatisfiable);
            }

            // Reading the archived bytes is an access event, whichever kind of payload it is and
            // however much of it was asked for.
            await audit.RecordAsync(AuditAction.SnapshotReplayed, nameof(Snapshot), id.ToString(), snapshot.Url, ct);

            if (outcome == RangeOutcome.Satisfiable)
            {
                response.StatusCode = StatusCodes.Status206PartialContent;
                response.Headers.ContentRange = $"bytes {from}-{to}/{total}";
                response.ContentLength = to - from + 1;

                await using var partial = await storage.GetRangeAsync(snapshot.PayloadObjectKey, from, to, ct);
                await partial.CopyToAsync(response.Body, ct);
                return Results.Empty;
            }

            response.ContentLength = total;

            await using var stream = await storage.GetAsync(snapshot.PayloadObjectKey, ct);
            await stream.CopyToAsync(response.Body, ct);
            return Results.Empty;
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
                .Include(s => s.PluginBindingVersion)
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

    private enum RangeOutcome
    {
        /// <summary>None was asked for, or one this endpoint does not serve. Send everything.</summary>
        None,

        Satisfiable,

        /// <summary>Asked for, and starting past the end of the object.</summary>
        Unsatisfiable,
    }

    /// <summary>
    /// Works out which bytes of an object of <paramref name="total"/> bytes were asked for.
    ///
    /// Only a single range is honoured. A replay viewer asks for one at a time, and a multipart
    /// response would be a good deal of machinery for a request nothing here makes; asking for
    /// several is answered with the whole object, which is allowed, if wasteful.
    /// </summary>
    private static RangeOutcome ResolveRange(HttpRequest request, long total, out long from, out long to)
    {
        from = 0;
        to = 0;

        var range = request.GetTypedHeaders().Range;

        if (range is null
            || !string.Equals(range.Unit.Value, "bytes", StringComparison.OrdinalIgnoreCase)
            || range.Ranges.Count != 1)
        {
            return RangeOutcome.None;
        }

        var asked = range.Ranges.Single();

        if (asked.From is { } start)
        {
            from = start;
            // An open-ended range runs to the end of the object, and an end stated past the end of
            // the object is clamped rather than refused, as HTTP requires.
            to = asked.To is { } end ? Math.Min(end, total - 1) : total - 1;
        }
        else if (asked.To is { } suffixLength)
        {
            // "bytes=-500": the last 500 bytes. This is how a viewer finds the directory of a
            // WACZ before it knows anything else about the file.
            from = Math.Max(0, total - suffixLength);
            to = total - 1;
        }
        else
        {
            return RangeOutcome.None;
        }

        return from < total && from <= to ? RangeOutcome.Satisfiable : RangeOutcome.Unsatisfiable;
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
    bool Timestamped,
    /// <summary>Plugin that produced this entry, or null for a page capture.</summary>
    string? Plugin);

public sealed record LedgerStatus(
    long ChainLength,
    string? HeadHash,
    DateTimeOffset? LastCapture,
    int SnapshotsAwaitingTimestamp,
    int TimestampAnchors,
    DateTimeOffset? LastAnchoredAt,
    bool LastAnchorQualified,
    WormSupport StorageWorm);
