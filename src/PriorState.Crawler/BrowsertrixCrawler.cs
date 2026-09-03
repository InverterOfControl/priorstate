using System.Globalization;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PriorState.Domain.Entities;
using PriorState.Domain.ValueObjects;

namespace PriorState.Crawler;

/// <summary>
/// Runs browsertrix-crawler in a container and collects the WACZ it produces.
///
/// Capture is deliberately not implemented here. The Webrecorder stack drives a real Chromium,
/// writes a specified archive format and is built for exactly this purpose; reimplementing it
/// would mean defending a home-grown capture mechanism in a dispute instead of pointing at an
/// established one. What this class adds is determinism and a record: settings come only from a
/// versioned profile, and the exact arguments used are handed back to be stored on the run.
/// </summary>
public sealed partial class BrowsertrixCrawler : ICrawler, IDisposable
{
    private readonly DockerClient _docker;
    private readonly CrawlerOptions _options;
    private readonly ILogger<BrowsertrixCrawler> _logger;

    public BrowsertrixCrawler(IOptions<CrawlerOptions> options, ILogger<BrowsertrixCrawler> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _logger = logger;
        _docker = new DockerClientConfiguration(new Uri(_options.DockerEndpoint)).CreateClient();
    }

    public async Task<CrawlOutcome> CaptureAsync(CrawlRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var collection = $"run-{request.RunId:n}";
        var arguments = BuildArguments(request, collection);

        // Joined once, unconditionally: the full command line is the thing an operator copies to
        // reproduce a capture by hand, so it is worth a string concatenation per crawl.
        var commandLine = string.Join(' ', arguments);
        LogStartingCrawl(request.RunId, _options.Image, commandLine);

        await EnsureImagePresentAsync(cancellationToken);

        var container = await _docker.Containers.CreateContainerAsync(
            new CreateContainerParameters
            {
                Image = _options.Image,
                Cmd = ["crawl", .. arguments],
                HostConfig = new HostConfig
                {
                    Binds = [$"{_options.HostWorkDirectory}:/crawls"],
                    AutoRemove = false,
                    // The crawl is untrusted input reaching a browser. Keep it away from the host.
                    NetworkMode = "bridge",
                    ShmSize = 1024L * 1024 * 1024,
                },
                Labels = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["org.priorstate.run"] = request.RunId.ToString(),
                    ["org.priorstate.profile"] = request.Profile.Designation,
                },
            },
            cancellationToken);

        try
        {
            await _docker.Containers.StartContainerAsync(container.ID, new ContainerStartParameters(), cancellationToken);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.Timeout);

            var wait = await _docker.Containers.WaitContainerAsync(container.ID, timeout.Token);
            var exitCode = (int)wait.StatusCode;
            var logs = await ReadLogTailAsync(container.ID, cancellationToken);

            var conditions = await ObserveConditionsAsync(request.Profile, cancellationToken);
            var waczPaths = CollectWaczPaths(collection);

            if (exitCode != 0)
            {
                LogCrawlFailed(request.RunId, exitCode);
                return new CrawlOutcome
                {
                    Succeeded = false,
                    ExitCode = exitCode,
                    Arguments = arguments,
                    ObservedConditions = conditions,
                    FailureReason = $"browsertrix-crawler exited with code {exitCode}.",
                    ContainerLogTail = logs,
                };
            }

            if (waczPaths.Count == 0)
            {
                return new CrawlOutcome
                {
                    Succeeded = false,
                    ExitCode = exitCode,
                    Arguments = arguments,
                    ObservedConditions = conditions,
                    FailureReason = "The crawl reported success but produced no WACZ file.",
                    ContainerLogTail = logs,
                };
            }

            LogCrawlSucceeded(request.RunId, waczPaths.Count);

            return new CrawlOutcome
            {
                Succeeded = true,
                ExitCode = exitCode,
                Arguments = arguments,
                WaczPaths = waczPaths,
                ObservedConditions = conditions,
                ContainerLogTail = logs,
            };
        }
        finally
        {
            try
            {
                await _docker.Containers.RemoveContainerAsync(
                    container.ID,
                    new ContainerRemoveParameters { Force = true },
                    CancellationToken.None);
            }
            catch (DockerApiException ex)
            {
                LogContainerCleanupFailed(container.ID, ex.Message);
            }
        }
    }

    /// <summary>
    /// Translates a capture profile into crawler arguments. Everything the browser does is
    /// decided here, from the profile alone — there is no per-run override, by design.
    /// </summary>
    private List<string> BuildArguments(CrawlRequest request, string collection)
    {
        var conditions = request.Profile.Conditions;
        var arguments = new List<string>
        {
            "--collection", collection,
            "--generateWACZ",
            // Text extraction feeds full-text search and the diff view. It is derived data and is
            // deliberately not part of the hash input.
            "--text", "to-pages",
            "--userAgent", conditions.UserAgent,
            "--screenWidth", conditions.ViewportWidth.ToString(CultureInfo.InvariantCulture),
            "--screenHeight", conditions.ViewportHeight.ToString(CultureInfo.InvariantCulture),
            "--pageLoadTimeout", "90",
            "--postLoadDelay",
            (conditions.JavaScriptSettleMs / 1000).ToString(CultureInfo.InvariantCulture),
            "--workers", _options.Workers.ToString(CultureInfo.InvariantCulture),
            "--pageLimit", _options.PageLimit.ToString(CultureInfo.InvariantCulture),
            "--delay", _options.DelayBetweenPagesSeconds.ToString(CultureInfo.InvariantCulture),
            "--logging", "stats",
        };

        foreach (var seed in request.SeedUrls)
        {
            arguments.Add("--url");
            arguments.Add(seed);
        }

        foreach (var include in request.ScopeIncludes)
        {
            arguments.Add("--include");
            arguments.Add(include);
        }

        foreach (var exclude in request.ScopeExcludes)
        {
            arguments.Add("--exclude");
            arguments.Add(exclude);
        }

        // Only the autoclick/banner behaviour is conditional, and only because the profile says
        // so. A capture that dismissed a banner and one that did not are different evidence, and
        // which happened is recorded either way.
        if (conditions.CookieBanner == CookieBannerHandling.Dismissed)
        {
            arguments.Add("--behaviors");
            arguments.Add("autoplay,autofetch,siteSpecific");
        }
        else
        {
            arguments.Add("--behaviors");
            arguments.Add("autoplay,autofetch");
        }

        return arguments;
    }

    private async Task<CaptureConditions> ObserveConditionsAsync(
        CaptureProfileVersion profile,
        CancellationToken cancellationToken)
    {
        var (chromium, crawler) = await ReadToolVersionsAsync(cancellationToken);

        return profile.Conditions with
        {
            ChromiumVersion = chromium,
            CrawlerVersion = crawler,
        };
    }

    /// <summary>
    /// Reads the versions out of the image itself rather than trusting configuration. What the
    /// evidence package states about the tooling has to be what actually ran.
    /// </summary>
    private async Task<(string Chromium, string Crawler)> ReadToolVersionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var image = await _docker.Images.InspectImageAsync(_options.Image, cancellationToken);
            var labels = image.Config?.Labels;

            var crawler = labels is not null && labels.TryGetValue("org.opencontainers.image.version", out var v)
                ? v
                : _options.Image;

            var chromium = labels is not null && labels.TryGetValue("org.webrecorder.browser.version", out var b)
                ? b
                : $"bundled with {_options.Image}";

            return (chromium, crawler);
        }
        catch (DockerApiException ex)
        {
            LogVersionLookupFailed(ex.Message);
            return ($"unknown ({_options.Image})", _options.Image);
        }
    }

    private List<string> CollectWaczPaths(string collection)
    {
        var directory = Path.Combine(_options.WorkDirectory, "collections", collection);

        return Directory.Exists(directory)
            ? [.. Directory.EnumerateFiles(directory, "*.wacz", SearchOption.AllDirectories).Order(StringComparer.Ordinal)]
            : [];
    }

    private async Task EnsureImagePresentAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _docker.Images.InspectImageAsync(_options.Image, cancellationToken);
            return;
        }
        catch (DockerImageNotFoundException)
        {
            LogPullingImage(_options.Image);
        }

        var reference = _options.Image.Split(':', 2);
        await _docker.Images.CreateImageAsync(
            new ImagesCreateParameters
            {
                FromImage = reference[0],
                Tag = reference.Length > 1 ? reference[1] : "latest",
            },
            authConfig: null,
            new Progress<JSONMessage>(),
            cancellationToken);
    }

    private async Task<string> ReadLogTailAsync(string containerId, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = await _docker.Containers.GetContainerLogsAsync(
                containerId,
                tty: false,
                new ContainerLogsParameters { ShowStdout = true, ShowStderr = true, Tail = "200" },
                cancellationToken);

            var (stdout, stderr) = await stream.ReadOutputToEndAsync(cancellationToken);
            return string.Concat(stdout, stderr);
        }
        catch (DockerApiException ex)
        {
            return $"(could not read container logs: {ex.Message})";
        }
    }

    public void Dispose() => _docker.Dispose();

    [LoggerMessage(
        EventId = 4000,
        Level = LogLevel.Information,
        Message = "Run {RunId}: starting {Image} with arguments: {Arguments}")]
    private partial void LogStartingCrawl(Guid runId, string image, string arguments);

    [LoggerMessage(
        EventId = 4001,
        Level = LogLevel.Information,
        Message = "Run {RunId}: crawl finished, {WaczCount} WACZ file(s) produced.")]
    private partial void LogCrawlSucceeded(Guid runId, int waczCount);

    [LoggerMessage(
        EventId = 4002,
        Level = LogLevel.Error,
        Message = "Run {RunId}: browsertrix-crawler exited with code {ExitCode}.")]
    private partial void LogCrawlFailed(Guid runId, int exitCode);

    [LoggerMessage(EventId = 4003, Level = LogLevel.Information, Message = "Pulling crawler image {Image}.")]
    private partial void LogPullingImage(string image);

    [LoggerMessage(
        EventId = 4004,
        Level = LogLevel.Warning,
        Message = "Could not read tool versions from the crawler image: {Reason}")]
    private partial void LogVersionLookupFailed(string reason);

    [LoggerMessage(
        EventId = 4005,
        Level = LogLevel.Warning,
        Message = "Could not remove crawl container {ContainerId}: {Reason}")]
    private partial void LogContainerCleanupFailed(string containerId, string reason);
}
