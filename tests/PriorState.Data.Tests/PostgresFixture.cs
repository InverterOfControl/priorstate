using Microsoft.EntityFrameworkCore;
using PriorState.Data;
using PriorState.Domain.Entities;
using PriorState.Domain.ValueObjects;
using PriorState.Ledger;
using Testcontainers.PostgreSql;

namespace PriorState.Data.Tests;

/// <summary>
/// A real PostgreSQL instance, with the real migrations applied.
///
/// These tests deliberately do not use the in-memory or SQLite providers. The guarantee being
/// tested — that the ledger cannot be altered — lives in Postgres triggers and grants, which no
/// other provider implements. A test that passed against a fake would be testing nothing.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("priorstate")
        .WithUsername("priorstate")
        .WithPassword("priorstate")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    public PriorStateDbContext CreateContext()
    {
        // EnableRetryOnFailure mirrors the production configuration in AddPriorStateData. It
        // matters: a retrying execution strategy makes EF reject user-initiated transactions,
        // which is exactly the path SnapshotLedger.AppendAsync takes. A fixture without it lets
        // that break in production while the tests stay green.
        var options = new DbContextOptionsBuilder<PriorStateDbContext>()
            .UseNpgsql(ConnectionString, npgsql =>
                npgsql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(2), null))
            .Options;

        return new PriorStateDbContext(options);
    }

    /// <summary>
    /// Inserts a project, a profile version, a run and one snapshot, and returns the snapshot.
    /// Each call creates its own project so tests do not interfere with one another.
    /// </summary>
    /// <summary>Seeds a project with one live plugin binding on it, for the append-only tests.</summary>
    public static async Task<PluginBindingVersion> SeedPluginBindingAsync(
        PriorStateDbContext db,
        string name = "erp-prices")
    {
        ArgumentNullException.ThrowIfNull(db);

        var profile = await db.CaptureProfileVersions.FirstOrDefaultAsync();
        if (profile is null)
        {
            profile = new CaptureProfileVersion
            {
                Name = "Test-Standard",
                Version = 1,
                Rationale = "Fixture profile.",
                Conditions = TestConditions,
            };
            db.CaptureProfileVersions.Add(profile);
            await db.SaveChangesAsync();
        }

        var project = new Project
        {
            Name = $"fixture-{Guid.CreateVersion7()}",
            SeedUrls = ["https://example.com/"],
            RetentionYears = 6,
            CaptureProfileVersionId = profile.Id,
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var binding = new PluginBindingVersion
        {
            ProjectId = project.Id,
            PluginId = "http-json",
            Name = name,
            Version = 1,
            ConfigurationJson = """{"url":"https://erp.example.com/api/prices"}""",
            SecretRef = "PS_SECRET_ERP_TOKEN",
            Rationale = "Fixture binding.",
            Required = false,
        };
        db.PluginBindingVersions.Add(binding);
        await db.SaveChangesAsync();

        return binding;
    }

    /// <summary>
    /// Appends a snapshot produced by a capture plugin: v2 canonical form, no browser conditions,
    /// and a binding whose digest is part of the entry hash.
    /// </summary>
    public static async Task<Snapshot> SeedPluginSnapshotAsync(
        PriorStateDbContext db,
        string url = "https://erp.example.com/api/prices")
    {
        ArgumentNullException.ThrowIfNull(db);

        var binding = await SeedPluginBindingAsync(db, $"fixture-{Guid.CreateVersion7():n}");
        var profile = await db.CaptureProfileVersions.FirstAsync();

        var run = new Run
        {
            ProjectId = binding.ProjectId,
            CaptureProfileVersionId = profile.Id,
            Trigger = RunTrigger.Manual,
            Status = RunStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
        };
        db.Runs.Add(run);
        await db.SaveChangesAsync();

        var snapshot = new Snapshot
        {
            RunId = run.Id,
            Url = url,
            CapturedAtUtc = DateTimeOffset.UtcNow,
            PayloadSha256 = Sha256Hash.Parse("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08"),
            PayloadObjectKey = $"test/{Guid.CreateVersion7():n}/payload.json",
            PayloadSizeBytes = 2048,
            PayloadMediaType = "application/json",
            CanonicalFormVersion = CanonicalSnapshotForm.Version2,
            CaptureProfileVersionId = profile.Id,
            CaptureProfileVersion = profile,
            Conditions = null,
            PluginBindingVersionId = binding.Id,
            PluginBindingVersion = binding,
            PluginVersion = "1.4.2",
            ChainSequence = 0,
            PreviousHash = Sha256Hash.Genesis,
            EntryHash = Sha256Hash.Genesis,
            StorageWorm = WormSupport.Unsupported,
        };

        await new SnapshotLedger(db).AppendAsync(snapshot);
        return snapshot;
    }

    public static async Task<Snapshot> SeedSnapshotAsync(PriorStateDbContext db, string url = "https://example.com/")
    {
        var profile = await db.CaptureProfileVersions.FirstOrDefaultAsync();
        if (profile is null)
        {
            profile = new CaptureProfileVersion
            {
                Name = "Test-Standard",
                Version = 1,
                Rationale = "Fixture profile.",
                Conditions = TestConditions,
            };
            db.CaptureProfileVersions.Add(profile);
            await db.SaveChangesAsync();
        }

        var project = new Project
        {
            Name = $"fixture-{Guid.CreateVersion7()}",
            SeedUrls = [url],
            RetentionYears = 6,
            CaptureProfileVersionId = profile.Id,
        };
        db.Projects.Add(project);

        var run = new Run
        {
            ProjectId = project.Id,
            CaptureProfileVersionId = profile.Id,
            Trigger = RunTrigger.Manual,
            Status = RunStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
        };
        db.Runs.Add(run);
        await db.SaveChangesAsync();

        var snapshot = new Snapshot
        {
            RunId = run.Id,
            Url = url,
            CapturedAtUtc = DateTimeOffset.UtcNow,
            PayloadSha256 = Sha256Hash.Parse("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"),
            PayloadObjectKey = $"test/{Guid.CreateVersion7():n}.wacz",
            PayloadSizeBytes = 4096,
            PayloadMediaType = "application/wacz",
            CanonicalFormVersion = CanonicalSnapshotForm.Version1,
            CaptureProfileVersionId = profile.Id,
            CaptureProfileVersion = profile,
            Conditions = TestConditions,
            ChainSequence = 0,
            PreviousHash = Sha256Hash.Genesis,
            EntryHash = Sha256Hash.Genesis,
            StorageWorm = WormSupport.Unsupported,
        };

        var ledger = new SnapshotLedger(db);
        return await ledger.AppendAsync(snapshot);
    }

    public static CaptureConditions TestConditions { get; } = new()
    {
        UserAgent = "PriorState-Test/1.0",
        ViewportWidth = 1920,
        ViewportHeight = 1080,
        AuthenticatedSession = false,
        AdBlockerActive = false,
        CookieBanner = CookieBannerHandling.LeftAsIs,
        JavaScriptSettleMs = 5000,
        ChromiumVersion = "140.0.0.0",
        CrawlerVersion = "1.7.1",
    };
}

[CollectionDefinition(nameof(PostgresCollection))]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
