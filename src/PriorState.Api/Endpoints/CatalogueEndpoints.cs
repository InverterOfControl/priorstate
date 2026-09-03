using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriorState.Api.Services;
using PriorState.Data;
using PriorState.Domain.Entities;

namespace PriorState.Api.Endpoints;

public static class CatalogueEndpoints
{
    public static void MapProjectEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/projects").RequireAuthorization().WithTags("Projects");

        group.MapGet("/", async (PriorStateDbContext db, CancellationToken ct) =>
            await db.Projects
                .AsNoTracking()
                .Include(p => p.CaptureProfileVersion)
                .OrderBy(p => p.Name)
                .Select(p => new ProjectSummary(
                    p.Id,
                    p.Name,
                    p.SeedUrls,
                    p.Schedule,
                    p.RetentionYears,
                    p.CaptureProfileVersion!.Designation,
                    p.Archived))
                .ToListAsync(ct));

        group.MapGet("/{id:guid}", async (Guid id, PriorStateDbContext db, CancellationToken ct) =>
            await db.Projects.AsNoTracking().Include(p => p.CaptureProfileVersion)
                .FirstOrDefaultAsync(p => p.Id == id, ct) is { } project
                ? Results.Ok(project)
                : Results.NotFound());

        group.MapPost("/", async (
            [FromBody] CreateProjectRequest request,
            PriorStateDbContext db,
            AuditLog audit,
            CancellationToken ct) =>
        {
            if (request.SeedUrls.Count == 0)
            {
                return Results.Problem("A project needs at least one seed URL.", statusCode: 400);
            }

            var profileId = request.CaptureProfileVersionId ?? DatabaseInitializer.StandardProfileId;
            if (!await db.CaptureProfileVersions.AnyAsync(p => p.Id == profileId, ct))
            {
                return Results.Problem($"Capture profile {profileId} does not exist.", statusCode: 400);
            }

            var project = new Project
            {
                Name = request.Name,
                SeedUrls = [.. request.SeedUrls],
                ScopeIncludes = [.. request.ScopeIncludes],
                ScopeExcludes = [.. request.ScopeExcludes],
                Schedule = request.Schedule,
                RetentionYears = request.RetentionYears,
                CaptureProfileVersionId = profileId,
            };

            db.Projects.Add(project);
            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(AuditAction.ProjectCreated, nameof(Project), project.Id.ToString(), project.Name, ct);

            return Results.Created($"/api/projects/{project.Id}", project);
        });

        // Retention can be extended, never shortened. Allowing an operator to shorten it after the
        // fact would let inconvenient snapshots be made to expire on a timer, which is the same
        // thing as deleting them, only slower.
        group.MapPost("/{id:guid}/retention", async (
            Guid id,
            [FromBody] ExtendRetentionRequest request,
            PriorStateDbContext db,
            AuditLog audit,
            CancellationToken ct) =>
        {
            var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (project is null)
            {
                return Results.NotFound();
            }

            if (request.RetentionYears <= project.RetentionYears)
            {
                return Results.Problem(
                    $"Retention can only be extended. The project is set to {project.RetentionYears} years; "
                    + $"{request.RetentionYears} would shorten or match it. Shortening retention is not "
                    + "supported, by design.",
                    statusCode: 400);
            }

            var previous = project.RetentionYears;
            project.RetentionYears = request.RetentionYears;
            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(
                AuditAction.RetentionExtended, nameof(Project), id.ToString(),
                $"{previous} -> {request.RetentionYears} years", ct);

            return Results.Ok(project);
        });
    }

    public static void MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/capture-profiles").RequireAuthorization().WithTags("Capture profiles");

        group.MapGet("/", async (PriorStateDbContext db, CancellationToken ct) =>
            await db.CaptureProfileVersions
                .AsNoTracking()
                .OrderBy(p => p.Name).ThenByDescending(p => p.Version)
                .ToListAsync(ct));

        // A profile is never edited. Changing capture settings creates a new version, which
        // applies only to captures made after it; snapshots keep the version they were taken
        // under, so an old protocol keeps describing what actually happened.
        group.MapPost("/", async (
            [FromBody] CreateProfileVersionRequest request,
            PriorStateDbContext db,
            AuditLog audit,
            CancellationToken ct) =>
        {
            var latest = await db.CaptureProfileVersions
                .Where(p => p.Name == request.Name)
                .OrderByDescending(p => p.Version)
                .FirstOrDefaultAsync(ct);

            var version = new CaptureProfileVersion
            {
                Name = request.Name,
                Version = (latest?.Version ?? 0) + 1,
                Rationale = request.Rationale,
                Conditions = request.Conditions,
            };

            db.CaptureProfileVersions.Add(version);

            if (latest is not null)
            {
                latest.SupersededAt = DateTimeOffset.UtcNow;
            }

            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(
                AuditAction.CaptureProfileVersionCreated, nameof(CaptureProfileVersion),
                version.Id.ToString(), version.Designation, ct);

            return Results.Created($"/api/capture-profiles/{version.Id}", version);
        });
    }
}

public sealed record ProjectSummary(
    Guid Id,
    string Name,
    IReadOnlyList<string> SeedUrls,
    string? Schedule,
    int RetentionYears,
    string CaptureProfile,
    bool Archived);

public sealed record CreateProjectRequest(
    string Name,
    IReadOnlyList<string> SeedUrls,
    IReadOnlyList<string> ScopeIncludes,
    IReadOnlyList<string> ScopeExcludes,
    string? Schedule,
    int RetentionYears,
    Guid? CaptureProfileVersionId);

public sealed record ExtendRetentionRequest(int RetentionYears);

public sealed record CreateProfileVersionRequest(
    string Name,
    string Rationale,
    Domain.ValueObjects.CaptureConditions Conditions);
