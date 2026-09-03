namespace PriorState.Domain.ValueObjects;

/// <summary>
/// The conditions a capture ran under. Recorded for every snapshot and reproduced in the evidence
/// package, because "what was the browser doing" is the first thing an opposing expert asks.
/// These are derived from the capture profile and the runtime, never set per run by a user.
/// </summary>
public sealed record CaptureConditions
{
    public required string UserAgent { get; init; }

    public required int ViewportWidth { get; init; }

    public required int ViewportHeight { get; init; }

    /// <summary>Always false in a valid capture: an authenticated view is not what a visitor saw.</summary>
    public required bool AuthenticatedSession { get; init; }

    /// <summary>Always false in a valid capture: blocking content changes the rendered page.</summary>
    public required bool AdBlockerActive { get; init; }

    public required CookieBannerHandling CookieBanner { get; init; }

    public required int JavaScriptSettleMs { get; init; }

    public required string ChromiumVersion { get; init; }

    public required string CrawlerVersion { get; init; }
}

public enum CookieBannerHandling
{
    /// <summary>Banner left as served. The default: least intervention, easiest to defend.</summary>
    LeftAsIs = 0,

    /// <summary>Banner dismissed via the crawler's behaviour, and the fact recorded.</summary>
    Dismissed = 1,
}
