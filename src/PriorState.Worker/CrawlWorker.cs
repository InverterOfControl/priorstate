using Microsoft.EntityFrameworkCore;
using PriorState.Crawler;
using PriorState.Data;
using PriorState.Domain.Entities;
using PriorState.Ledger;
using PriorState.Plugins;
using PriorState.Storage;

namespace PriorState.Worker;

/// <summary>
/// Claims queued crawls, runs them, and appends the result to the ledger.
///
/// Claiming uses FOR UPDATE SKIP LOCKED against the jobs table: several workers can run without a
/// broker, and the queue lives in the same database as the ledger so a job and the entry it
/// produces commit together.
/// </summary>
public sealed partial class CrawlWorker : BackgroundService
{
    private static readonly TimeSpan IdlePollInterval = TimeSpan.FromSeconds(5);

    /// <summary>The media type a page capture is stored and recorded under.</summary>
    private const string WaczMediaType = "application/wacz";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICrawler _crawler;
    private readonly IObjectStore _storage;
    private readonly ILogger<CrawlWorker> _logger;
    private readonly string _workerId = $"{Environment.MachineName}/{Environment.ProcessId}";

    public CrawlWorker(
        IServiceScopeFactory scopeFactory,
        ICrawler crawler,
        IObjectStore storage,
        ILogger<CrawlWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _crawler = crawler;
        _storage = storage;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogWorkerStarted(_workerId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await TryProcessOneAsync(stoppingToken))
                {
                    await Task.Delay(IdlePollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // The loop must survive any single job going wrong.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogWorkerLoopFailed(ex);
                await Task.Delay(IdlePollInterval, stoppingToken);
            }
        }
    }

    private async Task<bool> TryProcessOneAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PriorStateDbContext>();
        var ledger = scope.ServiceProvider.GetRequiredService<SnapshotLedger>();
        var plugins = scope.ServiceProvider.GetRequiredService<PluginRunner>();

        var jobId = await ClaimJobAsync(db, cancellationToken);
        if (jobId is null)
        {
            return false;
        }

        var job = await db.CrawlJobs.FirstAsync(j => j.Id == jobId, cancellationToken);
        var run = await db.Runs
            .Include(r => r.Project)
            .Include(r => r.CaptureProfileVersion)
            .FirstAsync(r => r.Id == job.RunId, cancellationToken);

        run.Status = RunStatus.Running;
        run.StartedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var project = run.Project!;
            var profile = run.CaptureProfileVersion!;

            var outcome = await _crawler.CaptureAsync(
                new CrawlRequest
                {
                    RunId = run.Id,
                    SeedUrls = project.SeedUrls,
                    ScopeIncludes = project.ScopeIncludes,
                    ScopeExcludes = project.ScopeExcludes,
                    Profile = profile,
                },
                cancellationToken);

            run.CrawlerArguments = [.. outcome.Arguments];
            run.CrawlerExitCode = outcome.ExitCode;

            if (!outcome.Succeeded)
            {
                await FailAsync(db, run, job, outcome.FailureReason ?? "The crawl failed.", cancellationToken);
                return true;
            }

            var retention = TimeSpan.FromDays(365.25 * project.RetentionYears);

            foreach (var waczPath in outcome.WaczPaths)
            {
                await AppendSnapshotAsync(ledger, run, profile, waczPath, outcome, retention, cancellationToken);
            }

            // Plugins run only once the page captures are in the ledger. A crawl produces the one
            // thing that cannot be fetched again later, and no plugin should be able to cost it.
            // A binding marked Required throws from here and fails the run through FailAsync.
            run.PluginFailures = [.. await plugins.RunAsync(run, profile, retention, cancellationToken)];

            run.Status = RunStatus.Succeeded;
            run.FinishedAt = DateTimeOffset.UtcNow;
            job.State = CrawlJobState.Completed;
            await db.SaveChangesAsync(cancellationToken);

            LogRunCompleted(run.Id, outcome.WaczPaths.Count);
            return true;
        }
#pragma warning disable CA1031 // Any failure has to be recorded on the run rather than lost.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogRunFailed(run.Id, ex);
            await FailAsync(db, run, job, ex.Message, cancellationToken);
            return true;
        }
    }

    private async Task AppendSnapshotAsync(
        SnapshotLedger ledger,
        Run run,
        CaptureProfileVersion profile,
        string waczPath,
        CrawlOutcome outcome,
        TimeSpan retention,
        CancellationToken cancellationToken)
    {
        var objectKey = $"projects/{run.ProjectId:n}/runs/{run.Id:n}/{Path.GetFileName(waczPath)}";

        await using var file = File.OpenRead(waczPath);
        var stored = await _storage.PutAsync(objectKey, file, WaczMediaType, retention, cancellationToken);

        var snapshot = new Snapshot
        {
            RunId = run.Id,
            Url = run.Project!.SeedUrls.FirstOrDefault() ?? string.Empty,
            CapturedAtUtc = run.StartedAt ?? DateTimeOffset.UtcNow,
            PayloadSha256 = stored.Sha256,
            PayloadObjectKey = stored.Key,
            PayloadSizeBytes = stored.SizeBytes,
            PayloadMediaType = WaczMediaType,
            CanonicalFormVersion = CanonicalSnapshotForm.Version1,
            CaptureProfileVersionId = profile.Id,
            CaptureProfileVersion = profile,
            // The conditions that actually applied, with tool versions read from the image that
            // ran rather than from the profile's expectations.
            Conditions = outcome.ObservedConditions,
            ChainSequence = 0,
            PreviousHash = Domain.ValueObjects.Sha256Hash.Genesis,
            EntryHash = Domain.ValueObjects.Sha256Hash.Genesis,
            StorageWorm = stored.Worm,
            WormRetainUntil = stored.RetainUntil,
        };

        await ledger.AppendAsync(snapshot, cancellationToken);
        LogSnapshotAppended(snapshot.ChainSequence, snapshot.Url, snapshot.EntryHash.Value);
    }

    /// <summary>
    /// Claims one job atomically. SKIP LOCKED means a second worker steps over a row another
    /// worker already holds instead of blocking on it.
    /// </summary>
    private async Task<Guid?> ClaimJobAsync(PriorStateDbContext db, CancellationToken cancellationToken)
    {
        var claimed = await db.Database.SqlQuery<Guid>($"""
            UPDATE crawl_jobs
               SET "State"     = 'Claimed',
                   "ClaimedBy" = {_workerId},
                   "ClaimedAt" = now(),
                   "Attempts"  = "Attempts" + 1
             WHERE "Id" = (
                     SELECT "Id"
                       FROM crawl_jobs
                      WHERE "State" = 'Pending'
                        AND "AvailableAt" <= now()
                      ORDER BY "AvailableAt"
                        FOR UPDATE SKIP LOCKED
                      LIMIT 1
                   )
            RETURNING "Id"
            """).ToListAsync(cancellationToken);

        return claimed.Count > 0 ? claimed[0] : null;
    }

    private static async Task FailAsync(
        PriorStateDbContext db, Run run, CrawlJob job, string reason, CancellationToken cancellationToken)
    {
        run.FailureReason = reason;
        run.FinishedAt = DateTimeOffset.UtcNow;
        job.LastError = reason;

        if (job.Attempts < job.MaxAttempts)
        {
            // Exponential back-off, so a site that is briefly down does not burn the attempts.
            job.State = CrawlJobState.Pending;
            job.AvailableAt = DateTimeOffset.UtcNow.AddMinutes(Math.Pow(3, job.Attempts));
            run.Status = RunStatus.Queued;
        }
        else
        {
            job.State = CrawlJobState.Failed;
            run.Status = RunStatus.Failed;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(EventId = 6000, Level = LogLevel.Information, Message = "Crawl worker {WorkerId} started.")]
    private partial void LogWorkerStarted(string workerId);

    [LoggerMessage(
        EventId = 6001, Level = LogLevel.Information,
        Message = "Run {RunId} completed with {SnapshotCount} snapshot(s).")]
    private partial void LogRunCompleted(Guid runId, int snapshotCount);

    [LoggerMessage(EventId = 6002, Level = LogLevel.Error, Message = "Run {RunId} failed.")]
    private partial void LogRunFailed(Guid runId, Exception exception);

    [LoggerMessage(
        EventId = 6003, Level = LogLevel.Information,
        Message = "Ledger entry {ChainSequence} appended for {Url}: {EntryHash}")]
    private partial void LogSnapshotAppended(long chainSequence, string url, string entryHash);

    [LoggerMessage(EventId = 6004, Level = LogLevel.Error, Message = "The crawl worker loop failed.")]
    private partial void LogWorkerLoopFailed(Exception exception);
}
