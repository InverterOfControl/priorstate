using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PriorState.Api.Services;
using PriorState.Data;
using PriorState.Domain.Entities;

namespace PriorState.Api.Endpoints;

public static class AuthEndpoints
{
    /// <summary>
    /// Wires up the Identity API and the few things it does not provide: a status endpoint the
    /// interface can call before it knows whether anyone is signed in, a sign-out endpoint, and a
    /// rule about who may create accounts.
    /// </summary>
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").WithTags("Authentication");

        auth.MapIdentityApi<ApplicationUser>();

        // Anonymous on purpose: the interface calls this on load to decide between showing the
        // sign-in form, the first-run account setup, or the application itself.
        auth.MapGet("/status", async (
            ClaimsPrincipal principal,
            UserManager<ApplicationUser> users,
            CancellationToken ct) =>
        {
            var authenticated = principal.Identity?.IsAuthenticated ?? false;

            return new AuthStatus(
                Authenticated: authenticated,
                UserName: authenticated ? principal.Identity?.Name : null,
                // Whether *any* account exists, which is what distinguishes a first run from a
                // locked instance. It leaks nothing an unauthenticated caller could not infer by
                // trying to register.
                HasUsers: await users.Users.AnyAsync(ct));
        }).AllowAnonymous();

        // MapIdentityApi has no sign-out, so cookie-based sessions would otherwise have no way to
        // end short of waiting for expiry.
        auth.MapPost("/logout", async (
            SignInManager<ApplicationUser> signInManager,
            AuditLog audit,
            CancellationToken ct) =>
        {
            await audit.RecordAsync(AuditAction.UserSignedOut, nameof(ApplicationUser), cancellationToken: ct);
            await signInManager.SignOutAsync();
            return Results.NoContent();
        }).RequireAuthorization();

        // Account creation rule, applied to the whole group so it also covers the /register
        // endpoint that MapIdentityApi contributes:
        //
        //   no accounts yet  -> anyone may create the first one (that is the initial setup)
        //   accounts exist   -> only a signed-in user may create another
        //
        // Without this, anything that can reach the port can create an account and read the
        // archive. For a system whose access log is supposed to establish who saw what, open
        // registration would make that log worthless.
        auth.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;

            var isRegister = HttpMethods.IsPost(http.Request.Method)
                && http.Request.Path.Value?.EndsWith("/register", StringComparison.OrdinalIgnoreCase) == true;

            if (!isRegister)
            {
                return await next(context);
            }

            if (http.User.Identity?.IsAuthenticated == true)
            {
                return await next(context);
            }

            var users = http.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
            if (await users.Users.AnyAsync(http.RequestAborted))
            {
                return Results.Problem(
                    "This instance already has an account. Sign in first — an existing user can create "
                    + "further accounts. Open registration is disabled so that the access log can "
                    + "establish who saw what.",
                    statusCode: StatusCodes.Status403Forbidden);
            }

            return await next(context);
        });
    }
}

public sealed record AuthStatus(bool Authenticated, string? UserName, bool HasUsers);
