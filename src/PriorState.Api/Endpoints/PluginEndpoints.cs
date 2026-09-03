using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriorState.Api.Services;
using PriorState.Data;
using PriorState.Domain.Entities;
using PriorState.Ledger;
using PriorState.Plugins;

namespace PriorState.Api.Endpoints;

public static class PluginEndpoints
{
    public static void MapPluginEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/plugins").RequireAuthorization().WithTags("Plugins");

        // What this build actually contains, at the versions actually loaded. Read from the
        // assemblies rather than from configuration, for the same reason the crawler reads
        // Chromium's version out of the running container.
        group.MapGet("/", (PluginCatalogue catalogue) =>
            catalogue.All
                .Select(p => new PluginSummary(p.Id, p.DisplayName, p.Version))
                .OrderBy(p => p.Id, StringComparer.Ordinal)
                .ToList());

        var bindings = app.MapGroup("/api/plugin-bindings").RequireAuthorization().WithTags("Plugins");

        bindings.MapGet("/", async (Guid? projectId, PriorStateDbContext db, CancellationToken ct) =>
        {
            var query = db.PluginBindingVersions.AsNoTracking();

            if (projectId is { } id)
            {
                query = query.Where(b => b.ProjectId == id);
            }

            return await query
                .OrderBy(b => b.Name).ThenByDescending(b => b.Version)
                .Select(b => new PluginBindingSummary(
                    b.Id,
                    b.ProjectId,
                    b.PluginId,
                    b.Name,
                    b.Version,
                    b.Designation,
                    b.ConfigurationJson,
                    b.SecretRef,
                    b.Rationale,
                    b.Required,
                    b.CreatedAt,
                    b.SupersededAt))
                .ToListAsync(ct);
        });

        // A binding is never edited. Changing where a plugin points creates a new version, which
        // applies only to runs after it; snapshots keep the version they ran under, so an old
        // protocol keeps describing what actually happened. Same rule as capture profiles, and
        // for the same reason: otherwise "you pointed it somewhere else" has no answer.
        bindings.MapPost("/", async (
            [FromBody] CreatePluginBindingRequest request,
            PriorStateDbContext db,
            PluginCatalogue catalogue,
            AuditLog audit,
            CancellationToken ct) =>
        {
            if (!catalogue.TryGet(request.PluginId, out _))
            {
                var known = string.Join(", ", catalogue.All.Select(p => p.Id).Order(StringComparer.Ordinal));
                return Results.Problem(
                    $"This build contains no capture plugin with the id '{request.PluginId}'. "
                    + $"Available plugins: {known}.",
                    statusCode: 400);
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.Problem("A binding needs a name. It is recorded in every evidence package "
                    + "the plugin produces, so choose one that will still mean something later.",
                    statusCode: 400);
            }

            if (string.IsNullOrWhiteSpace(request.Rationale))
            {
                return Results.Problem(
                    "A binding needs a rationale explaining why it exists. It is shown in the evidence "
                    + "package, and a change nobody could justify in writing is a change worth questioning.",
                    statusCode: 400);
            }

            if (!IsJsonObject(request.ConfigurationJson))
            {
                return Results.Problem(
                    "The configuration must be a JSON object. It is stored byte for byte, hashed into the "
                    + "snapshot's entry hash and shipped in the evidence package, so it has to be readable "
                    + "by whoever checks it.",
                    statusCode: 400);
            }

            // The name is recorded, the value never is. Rejecting anything outside the reserved
            // prefix stops a binding being pointed at the database connection string.
            if (request.SecretRef is { Length: > 0 } && !PluginSecretResolver.IsValidSecretRef(request.SecretRef))
            {
                return Results.Problem(
                    $"'{request.SecretRef}' is not a usable secret reference. Secrets are read from an "
                    + "environment variable named PS_SECRET_<NAME>, using capitals, digits and underscores. "
                    + "The name is recorded; the value never leaves the worker's environment.",
                    statusCode: 400);
            }

            if (!await db.Projects.AnyAsync(p => p.Id == request.ProjectId, ct))
            {
                return Results.NotFound();
            }

            var latest = await db.PluginBindingVersions
                .Where(b => b.ProjectId == request.ProjectId && b.Name == request.Name)
                .OrderByDescending(b => b.Version)
                .FirstOrDefaultAsync(ct);

            var binding = new PluginBindingVersion
            {
                ProjectId = request.ProjectId,
                PluginId = request.PluginId,
                Name = request.Name,
                Version = (latest?.Version ?? 0) + 1,
                ConfigurationJson = request.ConfigurationJson,
                SecretRef = string.IsNullOrWhiteSpace(request.SecretRef) ? null : request.SecretRef,
                Rationale = request.Rationale,
                Required = request.Required,
            };

            db.PluginBindingVersions.Add(binding);

            if (latest is not null && latest.SupersededAt is null)
            {
                latest.SupersededAt = DateTimeOffset.UtcNow;
            }

            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(
                AuditAction.PluginBindingVersionCreated, nameof(PluginBindingVersion),
                binding.Id.ToString(), $"{binding.PluginId} / {binding.Designation}", ct);

            return Results.Created($"/api/plugin-bindings/{binding.Id}", Describe(binding));
        });

        // Retiring is superseding without a successor. There is no delete: "this stopped running
        // on the 12th" is itself a fact the record has to keep.
        bindings.MapPost("/{id:guid}/retire", async (
            Guid id,
            PriorStateDbContext db,
            AuditLog audit,
            CancellationToken ct) =>
        {
            var binding = await db.PluginBindingVersions.FirstOrDefaultAsync(b => b.Id == id, ct);

            if (binding is null)
            {
                return Results.NotFound();
            }

            if (binding.SupersededAt is not null)
            {
                return Results.Problem(
                    $"Binding '{binding.Designation}' was already superseded on "
                    + $"{CanonicalSnapshotForm.FormatTimestamp(binding.SupersededAt.Value)}. That date is "
                    + "set once and cannot be changed.",
                    statusCode: 409);
            }

            binding.SupersededAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(
                AuditAction.PluginBindingRetired, nameof(PluginBindingVersion),
                binding.Id.ToString(), binding.Designation, ct);

            return Results.Ok(Describe(binding));
        });
    }

    private static bool IsJsonObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static PluginBindingSummary Describe(PluginBindingVersion b) => new(
        b.Id, b.ProjectId, b.PluginId, b.Name, b.Version, b.Designation, b.ConfigurationJson,
        b.SecretRef, b.Rationale, b.Required, b.CreatedAt, b.SupersededAt);
}

public sealed record PluginSummary(string Id, string DisplayName, string Version);

public sealed record PluginBindingSummary(
    Guid Id,
    Guid ProjectId,
    string PluginId,
    string Name,
    int Version,
    string Designation,
    string ConfigurationJson,
    string? SecretRef,
    string Rationale,
    bool Required,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SupersededAt);

public sealed record CreatePluginBindingRequest(
    Guid ProjectId,
    string PluginId,
    string Name,
    string ConfigurationJson,
    string? SecretRef,
    string Rationale,
    bool Required);
