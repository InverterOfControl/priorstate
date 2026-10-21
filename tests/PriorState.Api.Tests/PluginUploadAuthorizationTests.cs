using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PriorState.Api.Endpoints;
using PriorState.Api.Services;
using PriorState.Data;
using PriorState.Plugins;

namespace PriorState.Api.Tests;

public sealed class PluginUploadAuthorizationTests
{
    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("reader", HttpStatusCode.Forbidden)]
    [InlineData("operator", HttpStatusCode.BadRequest)]
    public async Task UploadRequiresAuthenticatedOperatorAndValidPackage(string? user, HttpStatusCode expected)
    {
        await using var app = CreateApp();
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/plugins/packages");
        if (user is not null) request.Headers.Add("X-Test-User", user);
        request.Headers.Add("X-PriorState-Plugin-Upload", "1");
        request.Content = new ByteArrayContent("invalid zip"u8.ToArray());
        request.Content.Headers.ContentType = new("application/octet-stream");
        using var response = await client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task OperatorUploadRequiresCustomCsrfHeader()
    {
        await using var app = CreateApp();
        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "operator");
        using var response = await client.PostAsync("/api/plugins/packages", new ByteArrayContent([]));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task StatusReportsWorkerMismatchAndPendingRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "priorstate-upload-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var app = CreateApp(directory);
            var store = app.Services.GetRequiredService<PluginPackageStore>();
            await store.WriteRuntimeAsync("worker", new(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                [new("git", new string('a', 64))]), CancellationToken.None);
            await app.StartAsync();
            using var client = app.GetTestClient();
            client.DefaultRequestHeaders.Add("X-Test-User", "operator");
            var state = await client.GetStringAsync("/api/plugins/packages");
            Assert.Contains("\"mismatch\":true", state, StringComparison.Ordinal);
            Assert.Contains("\"canInstall\":true", state, StringComparison.Ordinal);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void EmptyOperatorListNeverGrantsInstallation()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "operator")], "test"));
        Assert.False(PluginPackageEndpoints.IsOperator(user, new PluginPackageOptions()));
        var empty = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "")], "test"));
        Assert.False(PluginPackageEndpoints.IsOperator(empty, new PluginPackageOptions { OperatorIds = [""] }));
    }

    private static WebApplication CreateApp(string? directory = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("test", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<AuditLog>();
        builder.Services.AddDbContext<PriorStateDbContext>(o => o.UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused"));
        builder.Services.AddSingleton(new PluginPackageStore(Options.Create(new PluginPackageOptions
        { Directory = directory ?? Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), OperatorIds = ["operator"] })));
        builder.Services.AddPriorStatePlugins();
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapPluginPackageEndpoints();
        return app;
    }

    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var user = Request.Headers["X-Test-User"].ToString();
            if (user.Length == 0) return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user)], Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
