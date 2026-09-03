namespace PriorState.Crawler;

/// <summary>Configuration for capture. Bound from "Crawler".</summary>
public sealed class CrawlerOptions
{
    public const string SectionName = "Crawler";

    /// <summary>
    /// Pinned by digest, not by tag. The crawler version is recorded in every snapshot and
    /// printed in every evidence package, so "which browser produced this" has to be a question
    /// with one answer. A floating tag would make old protocols unreproducible.
    /// </summary>
    public string Image { get; set; } = "webrecorder/browsertrix-crawler:1.7.1";

    /// <summary>Host directory shared with the crawl container for its output.</summary>
    public string WorkDirectory { get; set; } = "/var/lib/priorstate/crawls";

    /// <summary>
    /// The same path as seen from the host, for the bind mount. Identical to
    /// <see cref="WorkDirectory"/> outside containerised deployments; in the bundled compose file
    /// it is the named volume's host path.
    /// </summary>
    public string HostWorkDirectory { get; set; } = "/var/lib/priorstate/crawls";

    public TimeSpan Timeout { get; set; } = TimeSpan.FromHours(2);

    /// <summary>Concurrent browser workers inside one crawl container.</summary>
    public int Workers { get; set; } = 2;

    /// <summary>Politeness delay between requests, in seconds. Crawl other people's sites gently.</summary>
    public int DelayBetweenPagesSeconds { get; set; } = 1;

    public int PageLimit { get; set; } = 500;

    /// <summary>Docker endpoint. The worker mounts the socket in the bundled compose file.</summary>
    public string DockerEndpoint { get; set; } = "unix:///var/run/docker.sock";
}
