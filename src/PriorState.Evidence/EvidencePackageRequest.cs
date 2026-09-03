using PriorState.Domain.Entities;
using PriorState.Domain.ValueObjects;

namespace PriorState.Evidence;

/// <summary>Everything needed to build one package, gathered by the caller in one transaction.</summary>
public sealed record EvidencePackageRequest
{
    /// <summary>Must have CaptureProfileVersion loaded — the canonical form cannot be built without it.</summary>
    public required Snapshot Snapshot { get; init; }

    /// <summary>The day's anchor. A snapshot that has not been anchored yet cannot be exported.</summary>
    public required TimestampAnchor Anchor { get; init; }

    /// <summary>Position of this snapshot among that day's entries, in chain order.</summary>
    public required int LeafIndex { get; init; }

    /// <summary>Sibling hashes from leaf to root, bottom-up.</summary>
    public required IReadOnlyList<Sha256Hash> AuditPath { get; init; }
}

/// <summary>Renders the protocol to PDF.</summary>
public interface IProtocolRenderer
{
    Task<byte[]> RenderAsync(
        EvidencePackageRequest request,
        Guid packageId,
        CancellationToken cancellationToken = default);
}

/// <summary>Configuration for evidence packages. Bound from "Evidence".</summary>
public sealed class EvidenceOptions
{
    public const string SectionName = "Evidence";

    /// <summary>Printed on every protocol, so a package can be traced to the build that made it.</summary>
    public string ToolVersion { get; set; } = "0.1.0-dev";

    /// <summary>
    /// PEM file with the timestamp authority's certificate chain, shipped inside every package so
    /// the token can be verified offline years later without the authority still being reachable.
    /// </summary>
    public string? CaChainPemPath { get; set; }

    /// <summary>Override the built-in German protocol template with a file of your own.</summary>
    public string? ProtocolTemplatePath { get; set; }

    /// <summary>
    /// Container image used to render the protocol to PDF. Defaults to the crawler image, because
    /// it already contains a Chromium and adding a second PDF toolchain to an evidence tool means
    /// one more thing to explain.
    /// </summary>
    public string RendererImage { get; set; } = "webrecorder/browsertrix-crawler:1.7.1";

    public string DockerEndpoint { get; set; } = "unix:///var/run/docker.sock";

    /// <summary>Shared directory for handing HTML to the renderer and collecting the PDF back.</summary>
    public string WorkDirectory { get; set; } = "/var/lib/priorstate/render";

    public string HostWorkDirectory { get; set; } = "/var/lib/priorstate/render";
}
