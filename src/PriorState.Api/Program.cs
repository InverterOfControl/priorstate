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
using PriorState.Plugins;
using PriorState.Plugins.HttpJson;
using PriorState.Storage;

var migrateOnly = args.Contains("--migrate", StringComparer.Ordinal);
var builder = WebApplication.CreateBuilder(args.Where(arg => arg != "--migrate").ToArray());

builder.Services.AddPriorStateData(builder.Configuration);
if (migrateOnly)
{
    builder.Services.AddScoped<DatabaseInitializer>();
    await using var migrationApp = builder.Build();
    await using var scope = migrationApp.Services.CreateAsyncScope();
    var password = builder.Configuration["Database:RuntimePassword"]
        ?? throw new InvalidOperationException("Database:RuntimePassword is required for --migrate.");
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitialiseAsync();
    await RuntimeDatabaseRole.ConfigureAsync(scope.ServiceProvider.GetRequiredService<PriorStateDbContext>(), password);
    return;
}
builder.Services.AddPriorStateStorage(builder.Configuration);

builder.Services.AddOptions<CrawlerOptions>()
    .Bind(builder.Configuration.GetSection(CrawlerOptions.SectionName));
builder.Services.AddOptions<EvidenceOptions>()
    .Bind(builder.Configuration.GetSection(EvidenceOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<EvidenceOptions>, EvidenceCertificateValidator>();
builder.Services.AddOptions<TimestampAuthorityOptions>()
    .Bind(builder.Configuration.GetSection(TimestampAuthorityOptions.SectionName));

// The API needs this too, not just the worker: on-demand anchoring contacts the authority from a
// request. Standard resilience because it is a third party over the public internet.
builder.Services.AddHttpClient<ITimestampAuthority, Rfc3161TimestampAuthority>()
    .AddStandardResilienceHandler();

builder.Services.AddSingleton<IProtocolRenderer, ChromiumProtocolRenderer>();
builder.Services.AddScoped<EvidencePackageBuilder>();
builder.Services.AddScoped<DatabaseInitializer>();
// The API does not execute plugins; it needs the catalogue so it can list what this build
// contains and refuse a binding naming a plugin that is not here. Registered from the same
// configuration as the worker so the two stay in step.
builder.Services.AddPriorStatePlugins();
builder.Services.AddHttpJsonCapturePlugin(builder.Configuration);

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

// Diagnose export configuration before accepting requests, including hosts without a configured bundle.
var evidenceOptions = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<EvidenceOptions>>().Value;
if (string.IsNullOrWhiteSpace(evidenceOptions.CaChainPemPath))
{
    LoggerMessage.Define(LogLevel.Warning, new EventId(5002, "MissingEvidenceCertificates"),
        "Evidence:CaChainPemPath is not configured. Evidence packages will omit TSA certificates; recipients must supply their own trusted CA bundle. See deploy/tsa-certificates.md.")(app.Logger, null);
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
app.MapPluginEndpoints();

// The Vue application is built to static files and served from here, so the production compose
// file needs no Node runtime and no second web server.
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

await app.RunAsync();

/// <summary>Exposed so the integration tests can drive the real host with WebApplicationFactory.</summary>
public partial class Program;
