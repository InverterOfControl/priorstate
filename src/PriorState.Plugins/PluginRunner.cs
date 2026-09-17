using Microsoft.EntityFrameworkCore;
using PriorState.Data;
using PriorState.Domain.Entities;
using PriorState.Domain.ValueObjects;
using PriorState.Ledger;
using PriorState.Plugins.Abstractions;
using PriorState.Storage;

namespace PriorState.Plugins;

/// <summary>Runs sources on the worker, sharing credentials and policy with connection tests.</summary>
public sealed class PluginRunner
{
    private readonly PriorStateDbContext _db;
    private readonly SnapshotLedger _ledger;
    private readonly IObjectStore _storage;
    private readonly PluginCatalogue _catalogue;
    private readonly PluginSecretResolver _secrets;

    public PluginRunner(PriorStateDbContext db, SnapshotLedger ledger, IObjectStore storage,
        PluginCatalogue catalogue, PluginSecretResolver secrets)
    {
        _db = db;
        _ledger = ledger;
        _storage = storage;
        _catalogue = catalogue;
        _secrets = secrets;
    }

    public async Task<IReadOnlyList<string>> RunAsync(Run run, CaptureProfileVersion profile,
        TimeSpan retention, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(profile);
        var startedAt = run.StartedAt ?? DateTimeOffset.UtcNow;
        // Freeze versions at the first attempt's start, including on retries.
        var bindings = await _db.PluginBindingVersions
            .Where(b => b.ProjectId == run.ProjectId && b.CreatedAt <= startedAt
                && (b.SupersededAt == null || b.SupersededAt > startedAt))
            .OrderBy(b => b.Name).ToListAsync(cancellationToken);
        var executions = await _db.SourceExecutions.Where(e => e.RunId == run.Id).ToListAsync(cancellationToken);
        foreach (var binding in bindings)
        {
            if (executions.All(e => e.BindingId != binding.Id))
            {
                var execution = new SourceExecution { RunId = run.Id, BindingId = binding.Id, Binding = binding };
                _db.SourceExecutions.Add(execution);
                executions.Add(execution);
            }
        }
        await _db.SaveChangesAsync(cancellationToken);
        var failures = new List<string>();
        foreach (var execution in executions)
        {
            if (execution.State == SourceExecutionState.Succeeded) continue;
            var binding = bindings.First(b => b.Id == execution.BindingId);
            execution.Binding = binding;
            await ExecuteAsync(execution, profile, retention, cancellationToken);
            if (execution.State == SourceExecutionState.Failed)
                failures.Add($"{binding.Designation}: {execution.Error}");
        }
        return failures;
    }

    public async Task TestAsync(Guid executionId, CancellationToken cancellationToken = default)
    {
        var execution = await _db.SourceExecutions.Include(e => e.Binding)
            .ThenInclude(b => b!.Project).ThenInclude(p => p!.CaptureProfileVersion)
            .SingleAsync(e => e.Id == executionId && e.RunId == null, cancellationToken);
        await ExecuteAsync(execution, execution.Binding!.Project!.CaptureProfileVersion!, TimeSpan.Zero, cancellationToken);
    }

    private async Task ExecuteAsync(SourceExecution execution, CaptureProfileVersion profile,
        TimeSpan retention, CancellationToken cancellationToken)
    {
        var binding = execution.Binding!;
        execution.State = SourceExecutionState.Running;
        execution.StartedAt = DateTimeOffset.UtcNow;
        execution.FinishedAt = null;
        execution.Error = null;
        await _db.SaveChangesAsync(cancellationToken);
        string? secret = null;
        try
        {
            if (!_catalogue.TryGet(binding.PluginId, out var registered))
                throw new PluginException("The source plugin is not installed on this worker.");
            secret = _secrets.Resolve(binding);
            var payload = await registered.Plugin.ExecuteAsync(new PluginExecutionContext
            {
                RunId = execution.RunId ?? Guid.Empty, ProjectId = binding.ProjectId,
                Profile = profile, Binding = binding, Secret = secret,
            }, cancellationToken);
            if (payload.Content.Length == 0) throw new PluginException("The API returned an empty response.");
            var capturedAt = payload.CapturedAtUtc ?? DateTimeOffset.UtcNow;
            execution.SizeBytes = payload.Content.Length;
            execution.MediaType = payload.MediaType;
            execution.FinishedAt = DateTimeOffset.UtcNow;
            execution.State = SourceExecutionState.Succeeded;
            if (execution.RunId is { } runId)
            {
                var objectKey = $"projects/{binding.ProjectId:n}/runs/{runId:n}/sources/{execution.Id:n}/{PayloadNaming.FileNameFor(payload.MediaType)}";
                using var content = new MemoryStream(payload.Content, writable: false);
                var stored = await _storage.PutAsync(objectKey, content, payload.MediaType, retention, cancellationToken);
                var snapshot = new Snapshot
                {
                    RunId = runId, Url = payload.Url, FinalUrl = payload.FinalUrl, CapturedAtUtc = capturedAt,
                    PayloadSha256 = stored.Sha256, PayloadObjectKey = stored.Key, PayloadSizeBytes = stored.SizeBytes,
                    PayloadMediaType = payload.MediaType, CanonicalFormVersion = CanonicalSnapshotForm.Version2,
                    CaptureProfileVersionId = profile.Id, CaptureProfileVersion = profile,
                    Conditions = null, PluginBindingVersionId = binding.Id, PluginBindingVersion = binding,
                    PluginVersion = registered.Version, ChainSequence = 0,
                    PreviousHash = Sha256Hash.Genesis, EntryHash = Sha256Hash.Genesis,
                    StorageWorm = stored.Worm, WormRetainUntil = stored.RetainUntil,
                };
                execution.SnapshotId = snapshot.Id;
                // The execution result and ledger append commit in the same transaction.
                await _ledger.AppendAsync(snapshot, cancellationToken);
            }
            else
            {
                // A connection test returns only metadata; its response is never archived.
                await _db.SaveChangesAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
#pragma warning disable CA1031 // Isolate a failing source while other sources and the website complete.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            foreach (var entry in _db.ChangeTracker.Entries<Snapshot>().Where(e => e.State == EntityState.Added).ToList())
                entry.State = EntityState.Detached;
            execution.State = SourceExecutionState.Failed;
            execution.SnapshotId = null;
            execution.FinishedAt = DateTimeOffset.UtcNow;
            var error = ex switch
            {
                OperationCanceledException => "The API request timed out.",
                PluginException => ex.Message,
                HttpRequestException => "The API could not be reached. Check its address, TLS certificate and worker network access.",
                _ => "The source could not be captured. Check worker storage and database availability.",
            };
            if (!string.IsNullOrEmpty(secret)) error = error.Replace(secret, "[redacted]", StringComparison.Ordinal);
            execution.Error = error.Length > 2000 ? error[..2000] : error;
            await _db.SaveChangesAsync(cancellationToken);
        }
    }
}
