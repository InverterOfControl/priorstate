using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PriorState.Api.Endpoints;
using PriorState.Api.Services;
using PriorState.Crawler;
using PriorState.Data;
using PriorState.Evidence;
using PriorState.Ledger.Timestamping;
using PriorState.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPriorStateData(builder.Configuration);
builder.Services.AddPriorStateStorage(builder.Configuration);

builder.Services.AddOptions<CrawlerOptions>()
    .Bind(builder.Configuration.GetSection(CrawlerOptions.SectionName));
builder.Services.AddOptions<EvidenceOptions>()
    .Bind(builder.Configuration.GetSection(EvidenceOptions.SectionName));
builder.Services.AddOptions<TimestampAuthorityOptions>()
    .Bind(builder.Configuration.GetSection(TimestampAuthorityOptions.SectionName));

builder.Services.AddSingleton<IProtocolRenderer, ChromiumProtocolRenderer>();
builder.Services.AddScoped<EvidencePackageBuilder>();
builder.Services.AddScoped<DatabaseInitializer>();
builder.Services.AddScoped<AuditLog>();
builder.Services.AddHttpContextAccessor();

// Local accounts so the tool can be evaluated with no identity provider; OIDC alongside for
// organisations that already have one. Either way an action reaches the audit log with a real
// identity attached, which the process documentation depends on.
builder.Services
    .AddIdentityApiEndpoints<ApplicationUser>(options =>
    {
        options.Password.RequiredLength = 12;
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedAccount = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddEntityFrameworkStores<PriorStateDbContext>();

var oidcSection = builder.Configuration.GetSection("Authentication:Oidc");
if (oidcSection.GetValue<bool>("Enabled"))
{
    builder.Services.AddAuthentication()
        .AddOpenIdConnect("oidc", options =>
        {
            options.Authority = oidcSection["Authority"];
            options.ClientId = oidcSection["ClientId"];
            options.ClientSecret = oidcSection["ClientSecret"];
            options.ResponseType = "code";
            options.SaveTokens = true;
            options.GetClaimsFromUserInfoEndpoint = true;
            options.Scope.Add("email");
            options.Scope.Add("profile");
        });
}

builder.Services.AddAuthorization();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database")
    .AddCheck<StorageHealthCheck>("storage");

var app = builder.Build();

// Migrations and the seeded capture profile run before the first request, so that a first
// `docker compose up` reaches a usable system with no manual step.
await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>()
        .InitialiseAsync(app.Lifetime.ApplicationStopping);
}

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapGroup("/api/auth").MapIdentityApi<ApplicationUser>();

app.MapProjectEndpoints();
app.MapSnapshotEndpoints();
app.MapRunEndpoints();
app.MapLedgerEndpoints();
app.MapProfileEndpoints();
app.MapAuditEndpoints();
app.MapWebhookEndpoints();

// The Vue application is built to static files and served from here, so the production compose
// file needs no Node runtime and no second web server.
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

await app.RunAsync();

/// <summary>Exposed so the integration tests can drive the real host with WebApplicationFactory.</summary>
public partial class Program;
