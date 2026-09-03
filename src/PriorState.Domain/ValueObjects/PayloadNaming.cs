namespace PriorState.Domain.ValueObjects;

/// <summary>
/// How a snapshot payload is named, from its media type alone.
///
/// Lives here because two places need the same answer and must not drift: the runner names the
/// object it stores, and the evidence package names the file it ships and prints that name in the
/// protocol. A protocol pointing at a file the package does not contain is the kind of defect
/// nobody notices until it is being read in front of an opponent.
///
/// Deliberately never derived from anything the remote end supplied. A filename taken from a
/// Content-Disposition header would be remote input choosing an object key.
/// </summary>
public static class PayloadNaming
{
    /// <summary>The name a page capture's WACZ is shipped under, unchanged since the first release.</summary>
    public const string PageCaptureFileName = "snapshot.wacz";

    /// <summary>"payload" plus an extension for the media type, e.g. "payload.json".</summary>
    public static string FileNameFor(string mediaType) => "payload" + ExtensionFor(mediaType);

    /// <summary>
    /// A deliberately tiny map. An unrecognised media type gets .bin rather than a guess: the
    /// media type itself is recorded in the canonical form, so nothing is lost by being dull here.
    /// </summary>
    public static string ExtensionFor(string mediaType)
    {
        ArgumentNullException.ThrowIfNull(mediaType);

        var bare = mediaType.Split(';')[0].Trim().ToLowerInvariant();

        if (bare.EndsWith("+json", StringComparison.Ordinal))
        {
            return ".json";
        }

        if (bare.EndsWith("+xml", StringComparison.Ordinal))
        {
            return ".xml";
        }

        return bare switch
        {
            "application/json" => ".json",
            "application/xml" or "text/xml" => ".xml",
            "text/csv" => ".csv",
            "text/plain" => ".txt",
            "text/html" => ".html",
            "application/pdf" => ".pdf",
            _ => ".bin",
        };
    }
}
