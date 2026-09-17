using Microsoft.EntityFrameworkCore;
using PriorState.Data;
using PriorState.Domain.Entities;
using PriorState.Plugins;

namespace PriorState.Worker;

/// <summary>Connection tests use worker network and secrets without creating ledger entries.</summary>
public sealed partial class SourceTestWorker(IServiceScopeFactory scopes, ILogger<SourceTestWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<PriorStateDbContext>();
                var expired = DateTimeOffset.UtcNow.AddMinutes(-2);
                await db.SourceExecutions.Where(e => e.RunId == null
                        && ((e.State == SourceExecutionState.Running && e.StartedAt < expired)
                            || (e.State == SourceExecutionState.Queued && e.QueuedAt < expired)))
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(e => e.State, SourceExecutionState.Failed)
                        .SetProperty(e => e.Error, "The worker did not complete this test. Check worker availability and test again.")
                        .SetProperty(e => e.FinishedAt, DateTimeOffset.UtcNow), stoppingToken);
                var ids = await db.Database.SqlQuery<Guid>($"""
                    UPDATE source_executions SET "State" = 'Running', "StartedAt" = now()
                    WHERE "Id" = (SELECT "Id" FROM source_executions
                        WHERE "RunId" IS NULL AND "State" = 'Queued'
                        ORDER BY "QueuedAt" FOR UPDATE SKIP LOCKED LIMIT 1)
                    RETURNING "Id"
                    """).ToListAsync(stoppingToken);
                if (ids.Count > 0)
                    await scope.ServiceProvider.GetRequiredService<PluginRunner>().TestAsync(ids[0], stoppingToken);
                else
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
#pragma warning disable CA1031 // Keep the queue alive after database or configuration failures.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogFailure(logger, ex);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "API source connection test worker failed.")]
    private static partial void LogFailure(ILogger logger, Exception exception);
}
