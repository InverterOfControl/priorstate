using PriorState.Data;

namespace PriorState.Worker;

/// <summary>
/// Anchors completed ledger entries to an external RFC-3161 timestamp on a schedule.
///
/// This is the step that makes the archive provable to someone who does not trust the operator.
/// Until entries are anchored they are internally consistent but rest on nothing external; once
/// anchored, altering any of them contradicts a signature the operator cannot forge.
///
/// Scheduled anchoring deliberately leaves the current UTC day alone: a token from a qualified
/// authority costs money per request, so a whole day goes into one Merkle tree. An operator who
/// needs an anchor sooner triggers one through the API, which anchors everything pending including
/// today. The two paths run the same code.
/// </summary>
public sealed partial class TimestampAnchorWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TimestampAnchorWorker> _logger;

    public TimestampAnchorWorker(IServiceScopeFactory scopeFactory, ILogger<TimestampAnchorWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Hourly rather than at a fixed time of day: an installation switched off overnight would
        // otherwise never anchor, and unanchored entries are the one backlog this system must not
        // accumulate. The first pass runs immediately on start.
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));

        do
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var anchors = scope.ServiceProvider.GetRequiredService<TimestampAnchorService>();

                await anchors.AnchorPendingAsync(includeToday: false, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // A failing authority must not stop the loop; entries stay pending.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogAnchoringFailed(ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(EventId = 6102, Level = LogLevel.Error, Message = "Timestamp anchoring failed.")]
    private partial void LogAnchoringFailed(Exception exception);
}
