using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriorState.Api.Services;
using PriorState.Data;
using PriorState.Domain.Entities;

namespace PriorState.Api.Endpoints;

public static class OperationsEndpoints
{
    public static void MapRunEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/runs").RequireAuthorization().WithTags("Runs");

        group.MapGet("/", async (Guid? projectId, PriorStateDbContext db, CancellationToken ct) =>
        {
            var query = db.Runs.AsNoTracking().Include(r => r.CaptureProfileVersion).AsQueryable();

            if (projectId is { } id)
            {
                query = query.Where(r => r.ProjectId == id);
            }

            return await query
                .OrderByDescending(r => r.QueuedAt)
                .Take(100)
                .Select(r => new RunSummary(
                    r.Id,
                    r.ProjectId,
                    r.Trigger,
                    r.Status,
                    r.QueuedAt,
                    r.StartedAt,
                    r.FinishedAt,
                    r.CaptureProfileVersion!.Designation,
                    r.Snapshots.Count,
                    r.FailureReason))
                .ToListAsync(ct);
        });

        group.MapGet("/{id:guid}", async (Guid id, PriorStateDbContext db, CancellationToken ct) =>
            await db.Runs.AsNoTracking()
                .Include(r => r.CaptureProfileVersion)
                .Include(r => r.Snapshots)
                .FirstOrDefaultAsync(r => r.Id == id, ct) is { } run
                ? Results.Ok(run)
                : Results.NotFound());

        group.MapPost("/", async (
            [FromBody] TriggerRunRequest request,
            PriorStateDbContext db,
            AuditLog audit,
            CancellationToken ct) =>
        {
            var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == request.ProjectId, ct);
            if (project is null)
            {
                return Results.NotFound();
            }

            var run = await QueueRunAsync(db, project, RunTrigger.Manual, ct);
            await audit.RecordAsync(AuditAction.RunTriggered, nameof(Run), run.Id.ToString(), project.Name, ct);

            return Results.Accepted($"/api/runs/{run.Id}", run);
        });
    }

    public static void MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/audit", async (
                string? subjectType,
                string? subjectId,
                int take,
                PriorStateDbContext db,
                CancellationToken ct) =>
            {
                var query = db.AuditLog.AsNoTracking().AsQueryable();

                if (!string.IsNullOrWhiteSpace(subjectType))
                {
                    query = query.Where(a => a.SubjectType == subjectType);
                }

                if (!string.IsNullOrWhiteSpace(subjectId))
                {
                    query = query.Where(a => a.SubjectId == subjectId);
                }

                return await query
                    .OrderByDescending(a => a.OccurredAtUtc)
                    .Take(take is > 0 and <= 1000 ? take : 200)
                    .ToListAsync(ct);
            })
            .RequireAuthorization()
            .WithTags("Audit");
    }

    public static void MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        // The bridge between "which code was deployed" and "what a visitor saw". Authenticated by
        // a shared secret rather than a user session, because it is called by CI, not by a person.
        app.MapPost("/api/webhooks/deployment", async (
                [FromBody] DeploymentNotification notification,
                [FromHeader(Name = "X-PriorState-Token")] string? token,
                IConfiguration configuration,
                PriorStateDbContext db,
                CancellationToken ct) =>
            {
                string? expected = configuration["Webhooks:DeploymentToken"];
                if (string.IsNullOrWhiteSpace(expected) || !CryptographicEquals(token, expected))
                {
                    return Results.Unauthorized();
                }

                var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == notification.ProjectId, ct);
                if (project is null)
                {
                    return Results.NotFound();
                }

                var run = await QueueRunAsync(db, project, RunTrigger.Deployment, ct);

                db.DeploymentLedgerEntries.Add(new DeploymentLedgerEntry
                {
                    ProjectId = project.Id,
                    CommitSha = notification.CommitSha,
                    CommitMessage = notification.CommitMessage,
                    Environment = notification.Environment,
                    DeployedAtUtc = notification.DeployedAtUtc,
                    Source = notification.Source ?? "webhook",
                    RunId = run.Id,
                });

                await db.SaveChangesAsync(ct);

                return Results.Accepted($"/api/runs/{run.Id}", new { runId = run.Id });
            })
            .AllowAnonymous()
            .WithTags("Webhooks");
    }

    /// <summary>
    /// Creates a run and its queue entry together. The worker claims jobs with
    /// FOR UPDATE SKIP LOCKED, so queueing needs no broker and shares a transaction with the run.
    /// </summary>
    private static async Task<Run> QueueRunAsync(
        PriorStateDbContext db,
        Project project,
        RunTrigger trigger,
        CancellationToken ct)
    {
        var run = new Run
        {
            ProjectId = project.Id,
            CaptureProfileVersionId = project.CaptureProfileVersionId,
            Trigger = trigger,
        };

        db.Runs.Add(run);
        db.CrawlJobs.Add(new CrawlJob { RunId = run.Id });
        await db.SaveChangesAsync(ct);

        return run;
    }

    /// <summary>Fixed-time comparison, so the webhook token cannot be recovered by timing.</summary>
    private static bool CryptographicEquals(string? provided, string expected) =>
        provided is not null
        && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(provided),
            System.Text.Encoding.UTF8.GetBytes(expected));
}

public sealed record RunSummary(
    Guid Id,
    Guid ProjectId,
    RunTrigger Trigger,
    RunStatus Status,
    DateTimeOffset QueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string CaptureProfile,
    int SnapshotCount,
    string? FailureReason);

public sealed record TriggerRunRequest(Guid ProjectId);

public sealed record DeploymentNotification(
    Guid ProjectId,
    string CommitSha,
    string? CommitMessage,
    string Environment,
    DateTimeOffset DeployedAtUtc,
    string? Source);
