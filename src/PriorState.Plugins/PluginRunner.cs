using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PriorState.Data;
using PriorState.Domain.Entities;
using PriorState.Domain.ValueObjects;
using PriorState.Ledger;
using PriorState.Plugins.Abstractions;
using PriorState.Storage;

namespace PriorState.Plugins;

/// <summary>
/// Runs the capture plugins bound to a project and appends what they returned to the ledger.
///
/// Everything that touches storage or the chain happens here rather than in the plugin, so a
/// plugin cannot influence how its output is hashed, cannot reach an existing entry, and cannot
/// produce a snapshot that the ledger did not build. A plugin returns bytes; this class decides
/// what those bytes mean.
///
/// Plugins run after the page captures have already been appended. A crawl produces the one thing
/// that cannot be obtained again later, and no plugin failure should be able to cost that.
/// </summary>
public sealed partial class PluginRunner
{
    private readonly PriorStateDbContext _db;
    private readonly SnapshotLedger _ledger;
    private readonly IObjectStore _storage;
    private readonly PluginCatalogue _catalogue;
    private readonly PluginSecretResolver _secrets;
    private readonly ILogger<PluginRunner> _logger;

    public PluginRunner(
        PriorStateDbContext db,
        SnapshotLedger ledger,
        IObjectStore storage,
        PluginCatalogue catalogue,
        PluginSecretResolver secrets,
        ILogger<PluginRunner> logger)
    {
        _db = db;
        _ledger = ledger;
        _storage = storage;
        _catalogue = catalogue;
        _secrets = secrets;
        _logger = logger;
    }

    /// <summary>
    /// Executes every live binding on the run's project.
    ///
    /// Returns the failures of bindings that are not Required, for the caller to record on the
    /// run. Throws <see cref="PluginException"/> for the first failure of a binding that is
    /// Required, which fails the run through the caller's normal failure path.
    /// </summary>
    public async Task<IReadOnlyList<string>> RunAsync(
        Run run,
        CaptureProfileVersion profile,
        TimeSpan retention,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(profile);

        var bindings = await _db.PluginBindingVersions
            .Where(b => b.ProjectId == run.ProjectId && b.SupersededAt == null)
            .OrderBy(b => b.Name)
            .ToListAsync(cancellationToken);

        if (bindings.Count == 0)
        {
            return [];
        }

        var failures = new List<string>();

        foreach (var binding in bindings)
        {
            try
            {
                await ExecuteBindingAsync(run, profile, binding, retention, cancellationToken);
            }
#pragma warning disable CA1031 // A plugin's failure is recorded, never allowed to escape as-is.
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                LogPluginFailed(run.Id, binding.Designation, ex);

                if (binding.Required)
                {
                    throw new PluginException(
                        $"The capture plugin binding '{binding.Designation}' is marked as required and failed: "
                        + ex.Message,
                        ex);
                }

                failures.Add($"{binding.Designation}: {ex.Message}");
            }
        }

        return failures;
    }

    private async Task ExecuteBindingAsync(
        Run run,
        CaptureProfileVersion profile,
        PluginBindingVersion binding,
        TimeSpan retention,
        CancellationToken cancellationToken)
    {
        if (!_catalogue.TryGet(binding.PluginId, out var registered))
        {
            throw new PluginException(
                $"Binding '{binding.Designation}' names the plugin '{binding.PluginId}', which this build does "
                + "not contain. The binding is left in place; snapshots taken under it keep their record.");
        }

        var context = new PluginExecutionContext
        {
            RunId = run.Id,
            ProjectId = run.ProjectId,
            Profile = profile,
            Binding = binding,
            Secret = _secrets.Resolve(binding),
        };

        var payload = await registered.Plugin.ExecuteAsync(context, cancellationToken);

        if (payload.Content.Length == 0)
        {
            // An empty payload would still hash and still append, producing an entry that asserts
            // an API returned nothing when in reality the plugin misbehaved. Refuse it.
            throw new PluginException(
                $"Plugin '{registered.Id}' returned an empty payload for binding '{binding.Designation}'. "
                + "An empty entry would be indistinguishable from a genuine empty response.");
        }

        var fileName = PayloadNaming.FileNameFor(payload.MediaType);
        var objectKey =
            $"projects/{run.ProjectId:n}/runs/{run.Id:n}/plugins/{registered.Id}/{binding.Id:n}/{fileName}";

        using var content = new MemoryStream(payload.Content, writable: false);
        var stored = await _storage.PutAsync(objectKey, content, payload.MediaType, retention, cancellationToken);

        var snapshot = new Snapshot
        {
            RunId = run.Id,
            Url = payload.Url,
            FinalUrl = payload.FinalUrl,
            CapturedAtUtc = DateTimeOffset.UtcNow,
            PayloadSha256 = stored.Sha256,
            PayloadObjectKey = stored.Key,
            PayloadSizeBytes = stored.SizeBytes,
            PayloadMediaType = payload.MediaType,
            CanonicalFormVersion = CanonicalSnapshotForm.Version2,
            CaptureProfileVersionId = profile.Id,
            CaptureProfileVersion = profile,
            // No browser conditions. An API call has none, and the v2 canonical form has no fields
            // that would need them.
            Conditions = null,
            PluginBindingVersionId = binding.Id,
            PluginBindingVersion = binding,
            // Observed from the assembly that ran, not from the binding or from configuration.
            PluginVersion = registered.Version,
            ChainSequence = 0,
            PreviousHash = Sha256Hash.Genesis,
            EntryHash = Sha256Hash.Genesis,
            StorageWorm = stored.Worm,
            WormRetainUntil = stored.RetainUntil,
        };

        await _ledger.AppendAsync(snapshot, cancellationToken);
        LogPluginSnapshotAppended(snapshot.ChainSequence, registered.Id, binding.Designation, snapshot.EntryHash.Value);
    }

    [LoggerMessage(
        EventId = 6300,
        Level = LogLevel.Information,
        Message = "Chain entry {Sequence}: plugin {PluginId} via binding {Binding} -> {EntryHash}.")]
    private partial void LogPluginSnapshotAppended(long sequence, string pluginId, string binding, string entryHash);

    [LoggerMessage(
        EventId = 6301,
        Level = LogLevel.Error,
        Message = "Run {RunId}: capture plugin binding {Binding} failed.")]
    private partial void LogPluginFailed(Guid runId, string binding, Exception exception);
}
