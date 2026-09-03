using Microsoft.EntityFrameworkCore;
using Npgsql;
using PriorState.Data;
using PriorState.Domain.Entities;
using PriorState.Domain.ValueObjects;

namespace PriorState.Data.Tests;

/// <summary>
/// The core guarantee, tested against the database rather than against application code.
///
/// If any test in this class starts failing, the archive has stopped being an archive. None of
/// these should ever be relaxed to make a feature work — the feature is what should change.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class AppendOnlyLedgerTests
{
    private readonly PostgresFixture _postgres;

    public AppendOnlyLedgerTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task Snapshot_CannotBeUpdated()
    {
        await using var db = _postgres.CreateContext();
        var snapshot = await PostgresFixture.SeedSnapshotAsync(db);

        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
            await db.Database.ExecuteSqlAsync(
                $"""UPDATE snapshots SET "Url" = 'https://tampered.example/' WHERE "Id" = {snapshot.Id}"""));

        Assert.Contains("append-only", ex.MessageText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Snapshot_CannotBeDeleted()
    {
        await using var db = _postgres.CreateContext();
        var snapshot = await PostgresFixture.SeedSnapshotAsync(db);

        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
            await db.Database.ExecuteSqlAsync($"""DELETE FROM snapshots WHERE "Id" = {snapshot.Id}"""));

        Assert.Contains("append-only", ex.MessageText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Snapshots_CannotBeTruncated()
    {
        await using var db = _postgres.CreateContext();
        await PostgresFixture.SeedSnapshotAsync(db);

        // TRUNCATE does not fire row-level triggers, so it needs a statement-level one of its own.
        // Without it, the single most effective way to destroy the ledger would go unblocked.
        await Assert.ThrowsAsync<PostgresException>(async () =>
            await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE snapshots CASCADE"));
    }

    [Fact]
    public async Task Snapshot_HashFieldsCannotBeRewritten()
    {
        await using var db = _postgres.CreateContext();
        var snapshot = await PostgresFixture.SeedSnapshotAsync(db);
        var forged = Sha256Hash.Parse(new string('a', 64));

        await Assert.ThrowsAsync<PostgresException>(async () =>
            await db.Database.ExecuteSqlAsync(
                $"""UPDATE snapshots SET "EntryHash" = {forged.Value} WHERE "Id" = {snapshot.Id}"""));
    }

    [Fact]
    public async Task Snapshot_TimestampAnchorCanBeSetExactlyOnce()
    {
        await using var db = _postgres.CreateContext();
        var snapshot = await PostgresFixture.SeedSnapshotAsync(db);

        var anchor = new TimestampAnchor
        {
            CoversFromUtc = snapshot.CapturedAtUtc,
            CoversUntilUtc = snapshot.CapturedAtUtc,
            FirstChainSequence = snapshot.ChainSequence,
            LastChainSequence = snapshot.ChainSequence,
            MerkleRoot = Sha256Hash.Parse(new string('b', 64)),
            TimestampToken = [1, 2, 3, 4],
            TsaUrl = "https://freetsa.org/tsr",
            TsaGeneralizedTime = DateTimeOffset.UtcNow,
            QualifiedProvider = false,
        };

        db.TimestampAnchors.Add(anchor);
        await db.SaveChangesAsync();

        // The one permitted update: filling in the anchor after the day has been timestamped.
        var affected = await db.Snapshots
            .Where(s => s.Id == snapshot.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.TimestampAnchorId, anchor.Id));
        Assert.Equal(1, affected);

        // And it is genuinely once: a second attempt is refused even though the column is the
        // same one. An anchored entry is closed.
        var second = new TimestampAnchor
        {
            CoversFromUtc = anchor.CoversFromUtc.AddDays(-400),
            CoversUntilUtc = anchor.CoversFromUtc.AddDays(-400),
            FirstChainSequence = 0,
            LastChainSequence = 0,
            MerkleRoot = Sha256Hash.Parse(new string('c', 64)),
            TimestampToken = [9],
            TsaUrl = "https://freetsa.org/tsr",
            TsaGeneralizedTime = DateTimeOffset.UtcNow,
            QualifiedProvider = false,
        };
        db.TimestampAnchors.Add(second);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<PostgresException>(async () =>
            await db.Database.ExecuteSqlAsync(
                $"""UPDATE snapshots SET "TimestampAnchorId" = {second.Id} WHERE "Id" = {snapshot.Id}"""));
    }

    [Fact]
    public async Task TimestampAnchor_CannotBeUpdatedOrDeleted()
    {
        await using var db = _postgres.CreateContext();

        var anchor = new TimestampAnchor
        {
            CoversFromUtc = new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero),
            CoversUntilUtc = new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero),
            FirstChainSequence = 1,
            LastChainSequence = 1,
            MerkleRoot = Sha256Hash.Parse(new string('d', 64)),
            TimestampToken = [7, 7, 7],
            TsaUrl = "https://freetsa.org/tsr",
            TsaGeneralizedTime = DateTimeOffset.UtcNow,
            QualifiedProvider = false,
        };
        db.TimestampAnchors.Add(anchor);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<PostgresException>(async () =>
            await db.Database.ExecuteSqlAsync(
                $"""UPDATE timestamp_anchors SET "TsaUrl" = 'https://elsewhere.example/' WHERE "Id" = {anchor.Id}"""));

        await Assert.ThrowsAsync<PostgresException>(async () =>
            await db.Database.ExecuteSqlAsync($"""DELETE FROM timestamp_anchors WHERE "Id" = {anchor.Id}"""));
    }

    [Fact]
    public async Task AuditLog_CannotBeUpdatedOrDeleted()
    {
        await using var db = _postgres.CreateContext();

        var entry = new AuditLogEntry
        {
            OccurredAtUtc = DateTimeOffset.UtcNow,
            Action = AuditAction.SnapshotViewed,
            SubjectType = nameof(Snapshot),
            SubjectId = Guid.CreateVersion7().ToString(),
        };
        db.AuditLog.Add(entry);
        await db.SaveChangesAsync();

        // An access log the operator can prune is not an access log.
        await Assert.ThrowsAsync<PostgresException>(async () =>
            await db.Database.ExecuteSqlAsync($"""DELETE FROM audit_log WHERE "Id" = {entry.Id}"""));

        await Assert.ThrowsAsync<PostgresException>(async () =>
            await db.Database.ExecuteSqlAsync(
                $"""UPDATE audit_log SET "UserName" = 'someone else' WHERE "Id" = {entry.Id}"""));
    }

    [Fact]
    public async Task CaptureProfileVersion_CannotHaveItsSettingsRewritten()
    {
        await using var db = _postgres.CreateContext();

        var profile = new CaptureProfileVersion
        {
            Name = $"Immutable-{Guid.CreateVersion7():n}",
            Version = 1,
            Rationale = "Original rationale.",
            Conditions = PostgresFixture.TestConditions,
        };
        db.CaptureProfileVersions.Add(profile);
        await db.SaveChangesAsync();

        // Rewriting a profile in place would make every protocol that names it describe settings
        // that were never used.
        await Assert.ThrowsAsync<PostgresException>(async () =>
            await db.Database.ExecuteSqlAsync(
                $"""
                UPDATE capture_profile_versions SET "Conditions_ViewportWidth" = 800 WHERE "Id" = {profile.Id}
                """));

        // Marking it superseded is the one permitted change.
        var affected = await db.CaptureProfileVersions
            .Where(p => p.Id == profile.Id)
            .ExecuteUpdateAsync(p => p.SetProperty(x => x.SupersededAt, DateTimeOffset.UtcNow));
        Assert.Equal(1, affected);
    }

    [Fact]
    public async Task OperationalTables_RemainMutable()
    {
        await using var db = _postgres.CreateContext();
        var snapshot = await PostgresFixture.SeedSnapshotAsync(db);
        var run = await db.Runs.FirstAsync(r => r.Id == snapshot.RunId);

        // Runs and jobs are operational state, not recorded history. They must stay editable, or
        // a retry could never be recorded.
        run.Status = RunStatus.Succeeded;
        run.FinishedAt = DateTimeOffset.UtcNow;

        var affected = await db.SaveChangesAsync();

        Assert.Equal(1, affected);
    }
}
