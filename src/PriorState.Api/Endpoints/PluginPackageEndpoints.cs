using System.Security.Claims;
using PriorState.Api.Services;
using PriorState.Domain.Entities;
using PriorState.Plugins;

namespace PriorState.Api.Endpoints;

public static class PluginPackageEndpoints
{
    public static bool IsOperator(ClaimsPrincipal user, PluginPackageOptions options) =>
        user.Identity?.IsAuthenticated == true && user.FindFirstValue(ClaimTypes.NameIdentifier) is { } id
        && !string.IsNullOrWhiteSpace(id) && options.OperatorIds.Contains(id, StringComparer.Ordinal);

    public static void MapPluginPackageEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/plugins/packages").RequireAuthorization().WithTags("Plugins");
        group.MapGet("/", (HttpContext http, PluginPackageStore store, PluginCatalogue catalogue) =>
        {
            var api = catalogue.All.Select(p => new PluginPackageIdentity(p.Id, p.PackageSha256)).ToArray();
            var installed = store.Installed;
            var worker = store.ReadRuntime("worker");
            var fresh = worker is not null && worker.SeenAt > DateTimeOffset.UtcNow.AddSeconds(-90);
            var mismatch = fresh && !api.OrderBy(p => p.Id, StringComparer.Ordinal)
                .SequenceEqual(worker!.Packages.OrderBy(p => p.Id, StringComparer.Ordinal));
            return Results.Ok(new
            {
                canInstall = IsOperator(http.User, store.Options),
                callerId = http.User.FindFirstValue(ClaimTypes.NameIdentifier),
                installed, loaded = api, worker, workerAvailable = fresh, mismatch,
                restartRequired = installed.Any(p => !api.Contains(p) || !fresh || !worker!.Packages.Contains(p)),
            });
        });
        group.MapPost("/", async (HttpContext http, PluginPackageStore store, AuditLog audit, CancellationToken ct) =>
        {
            if (!IsOperator(http.User, store.Options)) return Results.Forbid();
            // A custom header cannot be sent by a cross-origin HTML form. No cross-origin CORS
            // policy is enabled for this endpoint, so a browser preflight cannot authorise it.
            if (http.Request.Headers["X-PriorState-Plugin-Upload"] != "1")
                return Results.Problem("A same-origin plugin upload is required.", statusCode: 400);
            if (http.Request.ContentType != "application/octet-stream")
                return Results.Problem("Upload the .psplugin package as application/octet-stream.", statusCode: 415);
            if (http.Request.ContentLength > store.Options.MaxUploadBytes)
                return Results.Problem("Plugin upload is too large.", statusCode: 413);
            var sizeFeature = http.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
            if (sizeFeature is { IsReadOnly: false }) sizeFeature.MaxRequestBodySize = store.Options.MaxUploadBytes;
            try
            {
                var package = await store.InstallAsync(http.Request.Body, ct, identity =>
                    audit.RecordAsync(AuditAction.PluginPackageInstallationAuthorized, "PluginPackage", identity.Id,
                        $"Installation authorised for package SHA-256 {identity.Sha256}; restart required.", ct));
                return Results.Accepted("/api/plugins/packages", new { package, restartRequired = true });
            }
            catch (PluginPackageException ex) { return Results.Problem(ex.Message, statusCode: 400); }
            catch (IOException) { return Results.Problem("Plugin installation could not acquire or write the package store. Retry after checking storage.", statusCode: 409); }
        });
    }
}
