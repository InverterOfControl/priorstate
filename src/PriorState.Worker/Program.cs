using PriorState.Crawler;
using PriorState.Data;
using PriorState.Evidence;
using PriorState.Ledger.Timestamping;
using PriorState.Plugins;
using PriorState.Storage;
using PriorState.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddPriorStateData(builder.Configuration);
builder.Services.AddPriorStateStorage(builder.Configuration);

builder.Services.AddOptions<CrawlerOptions>()
    .Bind(builder.Configuration.GetSection(CrawlerOptions.SectionName));
builder.Services.AddOptions<EvidenceOptions>()
    .Bind(builder.Configuration.GetSection(EvidenceOptions.SectionName));
builder.Services.AddOptions<TimestampAuthorityOptions>()
    .Bind(builder.Configuration.GetSection(TimestampAuthorityOptions.SectionName));

builder.Services.AddSingleton<ICrawler, BrowsertrixCrawler>();

// Standard resilience: the timestamp authority is a third party over the public internet, and a
// transient failure there must not leave a day unanchored.
builder.Services.AddHttpClient<ITimestampAuthority, Rfc3161TimestampAuthority>()
    .AddStandardResilienceHandler();

// Capture plugins are loaded once at startup; each snapshot records its actual package hash.
builder.Services.AddPriorStatePlugins();
builder.Services.AddPluginPackages(builder.Configuration, "worker");

builder.Services.AddHostedService<CrawlWorker>();
builder.Services.AddHostedService<SourceTestWorker>();
builder.Services.AddHostedService<ScheduleWorker>();
builder.Services.AddHostedService<TimestampAnchorWorker>();

var host = builder.Build();
await host.RunAsync();
