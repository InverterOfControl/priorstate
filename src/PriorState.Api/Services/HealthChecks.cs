using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PriorState.Data;
using PriorState.Domain.Entities;
using PriorState.Storage;

namespace PriorState.Api.Services;

internal sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly PriorStateDbContext _db;

    public DatabaseHealthCheck(PriorStateDbContext db) => _db = db;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var pending = await _db.Database.GetPendingMigrationsAsync(cancellationToken);
            if (pending.Any())
            {
                return HealthCheckResult.Degraded($"{pending.Count()} migration(s) have not been applied.");
            }

            var entries = await _db.Snapshots.CountAsync(cancellationToken);
            return HealthCheckResult.Healthy($"{entries} ledger entries.");
        }
        catch (InvalidOperationException ex)
        {
            return HealthCheckResult.Unhealthy("The database is not reachable.", ex);
        }
    }
}

/// <summary>
/// Reports the storage endpoint and, importantly, what it was observed to do about immutability.
/// Surfaced on /health so an operator can see at a glance whether their backend actually enforces
/// WORM, rather than discovering it from an evidence package months later.
/// </summary>
internal sealed class StorageHealthCheck : IHealthCheck
{
    private readonly IObjectStore _storage;

    public StorageHealthCheck(IObjectStore storage) => _storage = storage;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var description = _storage.WormCapability switch
        {
            WormSupport.Enforced =>
                "Object Lock verified: the backend refused to delete an object under retention.",
            WormSupport.ApiPresentUnverified =>
                "Object Lock accepted but not enforced. Snapshots remain fully provable through the hash "
                + "chain and timestamps; evidence packages will say so rather than claim storage immutability.",
            WormSupport.Unsupported =>
                "No storage-level Object Lock. Snapshots remain fully provable through the hash chain and "
                + "timestamps. See the storage page in the documentation for backends that enforce it.",
            _ => "Unknown storage capability.",
        };

        // Only a genuine failure is unhealthy. A backend without WORM is a documented, supported
        // configuration, not a fault — reporting it as one would train operators to ignore it.
        return Task.FromResult(HealthCheckResult.Healthy(description));
    }
}
