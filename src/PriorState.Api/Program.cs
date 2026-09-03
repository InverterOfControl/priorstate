using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
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

// The API needs this too, not just the worker: on-demand anchoring contacts the authority from a
// request. Standard resilience because it is a third party over the public internet.
builder.Services.AddHttpClient<ITimestampAuthority, Rfc3161TimestampAuthority>()
    .AddStandardResilienceHandler();

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

// Enums go over the wire as names, not numbers. WormSupport in particular is read by a person as
// often as by code — it appears in the interface and in evidence packages — and "Unsupported" is
// self-explanatory where "0" is a trap.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Data protection keys encrypt the authentication cookies. Left in the container they are lost on
// every restart, which signs everyone out — and in a system whose audit log is supposed to record
// who accessed what, a forced re-authentication of every user on every deployment is noise in
// exactly the record that matters.
if (builder.Configuration["DataProtection:KeyPath"] is { Length: > 0 } keyPath)
{
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(keyPath))
        .SetApplicationName("PriorState");
}

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
app.MapAuthEndpoints();

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
