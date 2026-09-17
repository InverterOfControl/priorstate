using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PriorState.Crawler;
using PriorState.Domain.Entities;
using PriorState.Domain.ValueObjects;
using PriorState.Ledger;
using PriorState.Plugins;
using PriorState.Plugins.Abstractions;
using PriorState.Storage;
using PriorState.Worker;

namespace PriorState.Data.Tests;

public sealed class ApiSourceExecutionTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Theory]
    [InlineData(false, false, RunStatus.PartiallySucceeded)]
    [InlineData(true, false, RunStatus.Failed)]
    [InlineData(false, true, RunStatus.Failed)]
    public async Task SourcesRunAlongsideBrowserAndKeepSuccessfulEvidence(bool required, bool browserFails, RunStatus expected)
    {
        await using var db = postgres.CreateContext();
        var binding = await PostgresFixture.SeedPluginBindingAsync(db);
        var failure = new PluginBindingVersion
        {
            ProjectId = binding.ProjectId, PluginId = "http-json", Name = "failure", Version = 1,
            ConfigurationJson = "{}", Rationale = "Test failure", Required = required,
        };
        db.PluginBindingVersions.Add(failure);
        var project = await db.Projects.SingleAsync(p => p.Id == binding.ProjectId);
        var run = new Run { ProjectId = project.Id, CaptureProfileVersionId = project.CaptureProfileVersionId, Trigger = RunTrigger.Manual };
        db.Runs.Add(run);
        db.CrawlJobs.Add(new CrawlJob { RunId = run.Id, MaxAttempts = 1 });
        await db.SaveChangesAsync();

        var plugin = new FakePlugin();
        var store = new MemoryStore();
        var file = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(file, "website archive fixture");
            await using var services = Services(plugin, store);
            var crawler = new CoordinatedCrawler(plugin, file, browserFails);
            using var worker = new CrawlWorker(services.GetRequiredService<IServiceScopeFactory>(), crawler, store, NullLogger<CrawlWorker>.Instance);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            Assert.True(await worker.TryProcessOneAsync(deadline.Token));
            db.ChangeTracker.Clear();
            var completed = await db.Runs.Include(r => r.SourceExecutions).Include(r => r.Snapshots).SingleAsync(r => r.Id == run.Id);
            Assert.Equal(expected, completed.Status);
            Assert.Equal(2, completed.SourceExecutions.Count);
            Assert.Single(completed.SourceExecutions, e => e.State == SourceExecutionState.Failed);
            var apiSnapshot = Assert.Single(completed.Snapshots, s => s.PluginBindingVersionId != null);
            Assert.Equal(plugin.ReceivedAt, apiSnapshot.CapturedAtUtc);
            Assert.Equal(browserFails ? 1 : 2, completed.Snapshots.Count);
            Assert.True((await new SnapshotLedger(db).VerifyAsync()).IsIntact);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task ConnectionTestUsesWorkerSecretButDoesNotWriteStorageOrLedger()
    {
        await using var db = postgres.CreateContext();
        var binding = await PostgresFixture.SeedPluginBindingAsync(db);
        var test = new SourceExecution { BindingId = binding.Id };
        db.SourceExecutions.Add(test);
        await db.SaveChangesAsync();
        var before = await db.Snapshots.CountAsync();
        var plugin = new FakePlugin();
        plugin.BrowserStarted.TrySetResult();
        var store = new MemoryStore();
        await using var services = Services(plugin, store);
        using var worker = new SourceTestWorker(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<SourceTestWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!await db.SourceExecutions.AsNoTracking().AnyAsync(e => e.Id == test.Id && e.State == SourceExecutionState.Succeeded, deadline.Token))
                await Task.Delay(50, deadline.Token);
        }
        finally { await worker.StopAsync(CancellationToken.None); }
        db.ChangeTracker.Clear();
        var result = await db.SourceExecutions.SingleAsync(e => e.Id == test.Id);
        Assert.Equal(SourceExecutionState.Succeeded, result.State);
        Assert.Equal("worker-only-secret", plugin.SeenSecret);
        Assert.Null(result.SnapshotId);
        Assert.Empty(store.Objects);
        Assert.Equal(before, await db.Snapshots.CountAsync());
    }

    [Fact]
    public async Task RepeatingSourceProcessingDoesNotRefetchSuccessfulSnapshots()
    {
        await using var db = postgres.CreateContext();
        var binding = await PostgresFixture.SeedPluginBindingAsync(db);
        var project = await db.Projects.Include(p => p.CaptureProfileVersion).SingleAsync(p => p.Id == binding.ProjectId);
        var run = new Run { ProjectId = project.Id, CaptureProfileVersionId = project.CaptureProfileVersionId, Trigger = RunTrigger.Manual, StartedAt = DateTimeOffset.UtcNow };
        db.Runs.Add(run);
        await db.SaveChangesAsync();
        var plugin = new FakePlugin();
        plugin.BrowserStarted.TrySetResult();
        var runner = new PluginRunner(db, new SnapshotLedger(db), new MemoryStore(), new PluginCatalogue([plugin]), new PluginSecretResolver(_ => "worker-only-secret"));
        await runner.RunAsync(run, project.CaptureProfileVersion!, TimeSpan.FromDays(1));
        await runner.RunAsync(run, project.CaptureProfileVersion!, TimeSpan.FromDays(1));
        Assert.Equal(1, plugin.Calls);
        Assert.Single(await db.Snapshots.Where(s => s.RunId == run.Id).ToListAsync());
    }

    private ServiceProvider Services(FakePlugin plugin, MemoryStore store)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => postgres.CreateContext());
        services.AddScoped<SnapshotLedger>();
        services.AddScoped<PluginRunner>();
        services.AddSingleton(new PluginCatalogue([plugin]));
        services.AddSingleton(new PluginSecretResolver(_ => "worker-only-secret"));
        services.AddSingleton<IObjectStore>(store);
        return services.BuildServiceProvider();
    }

    private sealed class FakePlugin : ICapturePlugin
    {
        public string Id => "http-json";
        public string DisplayName => "Test API";
        public TaskCompletionSource BrowserStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ResponseReceived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DateTimeOffset ReceivedAt { get; } = new(2026, 9, 17, 8, 0, 0, TimeSpan.Zero);
        public int Calls { get; private set; }
        public string? SeenSecret { get; private set; }
        public async Task<PluginPayload> ExecuteAsync(PluginExecutionContext context, CancellationToken cancellationToken = default)
        {
            await BrowserStarted.Task.WaitAsync(cancellationToken);
            Calls++;
            SeenSecret = context.Secret;
            ResponseReceived.TrySetResult();
            if (context.Binding.Name == "failure") throw new PluginException("API unavailable");
            return new PluginPayload { Url = "https://api.example.test/prices", Content = "{\"price\":12}"u8.ToArray(), MediaType = "application/json", CapturedAtUtc = ReceivedAt };
        }
    }

    private sealed class CoordinatedCrawler(FakePlugin plugin, string file, bool fails) : ICrawler
    {
        public async Task<CrawlOutcome> CaptureAsync(CrawlRequest request, CancellationToken cancellationToken = default)
        {
            plugin.BrowserStarted.TrySetResult();
            await plugin.ResponseReceived.Task.WaitAsync(cancellationToken);
            return new CrawlOutcome { Succeeded = !fails, ExitCode = fails ? 1 : 0, Arguments = [], WaczPaths = fails ? [] : [file], ObservedConditions = request.Profile.Conditions, FailureReason = fails ? "Browser failed" : null };
        }
    }

    private sealed class MemoryStore : IObjectStore
    {
        public Dictionary<string, byte[]> Objects { get; } = new(StringComparer.Ordinal);
        public WormSupport WormCapability => WormSupport.Unsupported;
        public async Task<ObjectWriteResult> PutAsync(string key, Stream content, string contentType, TimeSpan retention, CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            var bytes = buffer.ToArray();
            Objects.Add(key, bytes);
            return new ObjectWriteResult { Key = key, SizeBytes = bytes.Length, Sha256 = Sha256Hash.FromBytes(SHA256.HashData(bytes)), Worm = WormSupport.Unsupported };
        }
        public Task<Stream> GetAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult<Stream>(new MemoryStream(Objects[key]));
        public Task<Stream> GetRangeAsync(string key, long firstByte, long lastByte, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(Objects.ContainsKey(key));
    }
}
