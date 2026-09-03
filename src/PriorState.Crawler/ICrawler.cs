using PriorState.Domain.Entities;
using PriorState.Domain.ValueObjects;

namespace PriorState.Crawler;

public interface ICrawler
{
    Task<CrawlOutcome> CaptureAsync(CrawlRequest request, CancellationToken cancellationToken = default);
}

public sealed record CrawlRequest
{
    public required Guid RunId { get; init; }

    public required IReadOnlyList<string> SeedUrls { get; init; }

    public IReadOnlyList<string> ScopeIncludes { get; init; } = [];

    public IReadOnlyList<string> ScopeExcludes { get; init; } = [];

    /// <summary>The profile version in force. Determines every browser-side setting.</summary>
    public required CaptureProfileVersion Profile { get; init; }
}

public sealed record CrawlOutcome
{
    public required bool Succeeded { get; init; }

    public required int ExitCode { get; init; }

    /// <summary>
    /// The exact `docker run` arguments used, recorded verbatim on the run so a third party can
    /// reproduce the capture by hand from the evidence package. Transparency here is worth more
    /// than tidiness.
    /// </summary>
    public required IReadOnlyList<string> Arguments { get; init; }

    /// <summary>Paths to the produced WACZ files, on the shared work directory.</summary>
    public IReadOnlyList<string> WaczPaths { get; init; } = [];

    /// <summary>
    /// The conditions the capture actually ran under, with the tool versions filled in from the
    /// container that ran rather than from the profile's expectations.
    /// </summary>
    public required CaptureConditions ObservedConditions { get; init; }

    public string? FailureReason { get; init; }

    public string? ContainerLogTail { get; init; }
}
