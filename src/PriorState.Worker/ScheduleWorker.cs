using Cronos;
using Microsoft.EntityFrameworkCore;
using PriorState.Data;
using PriorState.Domain.Entities;

namespace PriorState.Worker;

/// <summary>
/// Queues runs for projects that have a cron schedule.
///
/// Schedules are evaluated in UTC, deliberately: a capture time that shifts twice a year with
/// daylight saving is an avoidable argument about when something was recorded.
/// </summary>
public sealed partial class ScheduleWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduleWorker> _logger;

    public ScheduleWorker(IServiceScopeFactory scopeFactory, ILogger<ScheduleWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

        do
        {
            try
            {
                await QueueDueRunsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // One bad schedule must not stop every other project.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogScheduleLoopFailed(ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task QueueDueRunsAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PriorStateDbContext>();

        var now = DateTimeOffset.UtcNow;
        var projects = await db.Projects
            .Where(p => !p.Archived && p.Schedule != null)
            .ToListAsync(cancellationToken);

        foreach (var project in projects)
        {
            var schedule = project.Schedule;
            if (schedule is null || !CronExpression.TryParse(schedule, CronFormat.Standard, out var cron))
            {
                LogInvalidSchedule(project.Id, schedule ?? "(none)");
                continue;
            }

            var lastRun = await db.Runs
                .Where(r => r.ProjectId == project.Id && r.Trigger == RunTrigger.Scheduled)
                .OrderByDescending(r => r.QueuedAt)
                .Select(r => (DateTimeOffset?)r.QueuedAt)
                .FirstOrDefaultAsync(cancellationToken);

            // Look forward from the last scheduled run, or from a minute ago on first use, so a
            // restart does not replay a backlog of every missed occurrence.
            var after = lastRun ?? now.AddMinutes(-1);
            var next = cron.GetNextOccurrence(after.UtcDateTime, TimeZoneInfo.Utc);

            if (next is null || next > now.UtcDateTime)
            {
                continue;
            }

            // A project already waiting in the queue does not get a second entry: a slow crawl
            // must not stack up behind itself.
            var alreadyQueued = await db.CrawlJobs
                .AnyAsync(
                    j => j.Run!.ProjectId == project.Id
                         && (j.State == CrawlJobState.Pending || j.State == CrawlJobState.Claimed),
                    cancellationToken);

            if (alreadyQueued)
            {
                LogSkippedBecauseBusy(project.Id);
                continue;
            }

            var run = new Run
            {
                ProjectId = project.Id,
                CaptureProfileVersionId = project.CaptureProfileVersionId,
                Trigger = RunTrigger.Scheduled,
            };

            db.Runs.Add(run);
            db.CrawlJobs.Add(new CrawlJob { RunId = run.Id });
            await db.SaveChangesAsync(cancellationToken);

            LogRunQueued(project.Id, project.Name, run.Id);
        }
    }

    [LoggerMessage(
        EventId = 6200, Level = LogLevel.Information,
        Message = "Queued scheduled run {RunId} for project {ProjectName} ({ProjectId}).")]
    private partial void LogRunQueued(Guid projectId, string projectName, Guid runId);

    [LoggerMessage(
        EventId = 6201, Level = LogLevel.Warning,
        Message = "Project {ProjectId} has an unparseable cron schedule '{Schedule}' and will not run on a "
                  + "timer until it is corrected.")]
    private partial void LogInvalidSchedule(Guid projectId, string schedule);

    [LoggerMessage(
        EventId = 6202, Level = LogLevel.Information,
        Message = "Project {ProjectId} is due but a run is still in progress; skipping this occurrence.")]
    private partial void LogSkippedBecauseBusy(Guid projectId);

    [LoggerMessage(EventId = 6203, Level = LogLevel.Error, Message = "The schedule loop failed.")]
    private partial void LogScheduleLoopFailed(Exception exception);
}
