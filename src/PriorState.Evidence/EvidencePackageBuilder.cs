using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PriorState.Domain.Entities;
using PriorState.Domain.ValueObjects;
using PriorState.Ledger;
using PriorState.Storage;

namespace PriorState.Evidence;

/// <summary>
/// Assembles the evidence package: everything a third party needs to check the claim, and nothing
/// they need to take on trust.
///
/// The package is the point at which this tool becomes useful to a lawyer rather than to an
/// engineer. It contains the archive, the exact bytes that were hashed, the timestamp token, the
/// audit path linking one to the other, a human-readable protocol, and the script that
/// recomputes all of it. Anyone can re-run the verification without contacting the operator.
/// </summary>
public sealed partial class EvidencePackageBuilder
{
    private readonly IObjectStore _storage;
    private readonly IProtocolRenderer _protocolRenderer;
    private readonly EvidenceOptions _options;
    private readonly ILogger<EvidencePackageBuilder> _logger;

    public EvidencePackageBuilder(
        IObjectStore storage,
        IProtocolRenderer protocolRenderer,
        IOptions<EvidenceOptions> options,
        ILogger<EvidencePackageBuilder> logger)
    {
        _storage = storage;
        _protocolRenderer = protocolRenderer;
        _options = options.Value;
        _logger = logger;
    }

    public async Task BuildAsync(
        EvidencePackageRequest request,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(destination);

        var snapshot = request.Snapshot;
        var packageId = Guid.CreateVersion7();

        // The canonical bytes are regenerated rather than stored, and then checked against the
        // recorded hash. If regeneration no longer reproduces the committed hash, the package is
        // refused: shipping an unverifiable package would be worse than shipping none.
        var canonical = CanonicalSnapshotForm.Render(snapshot);
        var recomputed = Sha256Hash.FromBytes(System.Security.Cryptography.SHA256.HashData(canonical));
        if (recomputed != snapshot.EntryHash)
        {
            throw new InvalidOperationException(
                $"Snapshot {snapshot.Id} does not reproduce its recorded entry hash "
                + $"({snapshot.EntryHash} recorded, {recomputed} recomputed). The record has been altered; "
                + "no evidence package will be produced for it.");
        }

        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);

        await WriteEntryAsync(archive, "canonical/entry.txt", canonical, cancellationToken);
        await WriteTextAsync(archive, "manifest.txt", BuildManifest(request, packageId), cancellationToken);
        await WriteTextAsync(archive, "merkle/audit-path.txt", BuildAuditPath(request), cancellationToken);
        await WriteTextAsync(archive, "timestamp/root.txt", request.Anchor.MerkleRoot.Value + "\n", cancellationToken);
        await WriteEntryAsync(archive, "timestamp/token.tsr", request.Anchor.TimestampToken, cancellationToken);
        await WriteTextAsync(archive, "README.txt", BuildReadme(request), cancellationToken);

        await WriteEmbeddedAsync(archive, "verify.sh", "PriorState.Evidence.Resources.verify.sh", cancellationToken);

        if (_options.CaChainPemPath is { Length: > 0 } chainPath && File.Exists(chainPath))
        {
            await WriteEntryAsync(
                archive, "timestamp/tsa-chain.pem", await File.ReadAllBytesAsync(chainPath, cancellationToken),
                cancellationToken);
        }
        else
        {
            LogMissingCaChain();
        }

        // For a plugin snapshot, the configuration it ran under is part of the claim. The binding
        // form is what binding_digest in canonical/entry.txt commits to, and the configuration is
        // shipped verbatim so the recipient can read it and recompute config_sha256 themselves.
        if (snapshot.PluginBindingVersion is { } binding)
        {
            await WriteEntryAsync(
                archive,
                EvidencePackageLayout.PluginBinding,
                CanonicalPluginBindingForm.Render(binding),
                cancellationToken);

            await WriteEntryAsync(
                archive,
                EvidencePackageLayout.PluginConfiguration,
                CanonicalPluginBindingForm.ConfigurationBytes(binding),
                cancellationToken);
        }

        var protocolPdf = await _protocolRenderer.RenderAsync(request, packageId, cancellationToken);
        await WriteEntryAsync(archive, "protocol.pdf", protocolPdf, cancellationToken);

        // The payload goes in last: it is by far the largest member, and writing it after the
        // small files means a truncated download is obvious rather than subtle.
        var payloadEntry = archive.CreateEntry(
            EvidencePackageLayout.PayloadFileName(snapshot), CompressionLevel.NoCompression);
        await using (var entryStream = payloadEntry.Open())
        await using (var payload = await _storage.GetAsync(snapshot.PayloadObjectKey, cancellationToken))
        {
            await payload.CopyToAsync(entryStream, cancellationToken);
        }

        LogPackageBuilt(snapshot.Id, packageId);
    }

    private string BuildManifest(EvidencePackageRequest request, Guid packageId)
    {
        var s = request.Snapshot;
        var a = request.Anchor;
        var manifest = new StringBuilder();

        manifest.Append("priorstate-evidence-package-v1\n");
        Line(manifest, "package_id", packageId.ToString());
        Line(manifest, "generated_at", CanonicalSnapshotForm.FormatTimestamp(DateTimeOffset.UtcNow));
        Line(manifest, "tool_version", _options.ToolVersion);
        Line(manifest, "canonical_form", s.CanonicalFormVersion);
        Line(manifest, "payload_file", EvidencePackageLayout.PayloadFileName(s));
        Line(manifest, "payload_media_type", s.PayloadMediaType);
        Line(manifest, "snapshot_id", s.Id.ToString());
        Line(manifest, "chain_sequence", s.ChainSequence.ToString(CultureInfo.InvariantCulture));
        Line(manifest, "entry_hash", s.EntryHash.Value);
        Line(manifest, "previous_hash", s.PreviousHash.Value);
        Line(manifest, "merkle_root", a.MerkleRoot.Value);
        Line(manifest, "anchor_covers_from", CanonicalSnapshotForm.FormatTimestamp(a.CoversFromUtc));
        Line(manifest, "anchor_covers_until", CanonicalSnapshotForm.FormatTimestamp(a.CoversUntilUtc));
        Line(manifest, "anchor_first_sequence", a.FirstChainSequence.ToString(CultureInfo.InvariantCulture));
        Line(manifest, "anchor_last_sequence", a.LastChainSequence.ToString(CultureInfo.InvariantCulture));
        Line(manifest, "tsa_url", a.TsaUrl);
        Line(manifest, "tsa_time", CanonicalSnapshotForm.FormatTimestamp(a.TsaGeneralizedTime));
        Line(manifest, "tsa_qualified", a.QualifiedProvider ? "yes" : "no (evaluation only)");
        Line(manifest, "storage_worm", s.StorageWorm.ToString());

        if (s.PluginBindingVersion is { } binding)
        {
            Line(manifest, "plugin", binding.PluginId);
            Line(manifest, "plugin_version", s.PluginVersion ?? "unknown");
            Line(manifest, "binding", binding.Designation);
            Line(manifest, "binding_digest", CanonicalPluginBindingForm.ComputeDigest(binding).Value);
        }

        return manifest.ToString();

        static void Line(StringBuilder builder, string key, string value) =>
            builder.Append(key).Append('=').Append(value).Append('\n');
    }

    /// <summary>
    /// The audit path, one sibling per line, prefixed with the side it sits on. The side has to be
    /// written down: hashing a pair in the wrong order produces a different node, and a verifier
    /// working from the hash alone cannot tell which way round they went.
    /// </summary>
    private static string BuildAuditPath(EvidencePackageRequest request)
    {
        var builder = new StringBuilder();
        var position = request.LeafIndex;

        foreach (var sibling in request.AuditPath)
        {
            builder.Append(position % 2 == 0 ? 'R' : 'L').Append(' ').Append(sibling.Value).Append('\n');
            position /= 2;
        }

        return builder.ToString();
    }

    private string BuildReadme(EvidencePackageRequest request)
    {
        var snapshot = request.Snapshot;
        var isPlugin = snapshot.PluginBindingVersion is not null;

        var payloadLines = isPlugin
            ? $"""
                 {EvidencePackageLayout.PayloadFileName(snapshot),-22}The archived response, exactly as the endpoint sent it.
                 {EvidencePackageLayout.PluginBinding,-22}The plugin configuration this ran under, as hashed.
                 {EvidencePackageLayout.PluginConfiguration,-22}That configuration in full, for reading.
               """
            : $"  {EvidencePackageLayout.PageCapturePayload,-22}The web archive. Open at https://replayweb.page (works offline).";

        var scope = isPlugin
            ? $"""
               Proved: the payload is unaltered since it was recorded, it was fetched from the URL above under
               the plugin configuration shipped in {EvidencePackageLayout.PluginConfiguration}, and it existed in
               exactly this form before the time attested by the timestamp authority.

               Not proved: that what the endpoint returned was correct. This attests receipt, not truth.
               """
            : """
              Proved: the archive is unaltered since it was recorded, and it existed in exactly this
              form before the time attested by the timestamp authority.

              Not proved: that the capture was complete or representative of the whole site. Judge that
              from snapshot.wacz and the capture conditions recorded in canonical/entry.txt.
              """;

        return $"""
        PriorState evidence package
        ===========================

        URL       {snapshot.Url}
        Captured  {CanonicalSnapshotForm.FormatTimestamp(snapshot.CapturedAtUtc)} UTC
        Profile   {snapshot.CaptureProfileVersion?.Designation ?? "unknown"}

        What is in here
        ---------------

          protocol.pdf           Human-readable record. Start here.
          verify.sh              Re-derives every claim in the protocol. Read it, then run it.
        {payloadLines}
          canonical/entry.txt    The exact bytes that were hashed into the ledger.
          manifest.txt           The same facts, machine-readable.
          merkle/audit-path.txt  Proof that this entry belongs to the timestamped root.
          timestamp/token.tsr    RFC-3161 token from an independent authority.
          timestamp/root.txt     The value that token attests to.
          timestamp/tsa-chain.pem  The authority's certificates, for offline verification.

        How to check it yourself
        ------------------------

            sh verify.sh

        Requires a POSIX shell, openssl, xxd and sha256sum. It contacts nothing over the network
        and trusts nothing about the system that produced this package. Exit code 0 means every
        check passed.

        What this proves, and what it does not
        --------------------------------------

        {scope}

        Generated by PriorState {_options.ToolVersion}, AGPL-3.0-only.
        https://github.com/InverterOfControl/priorstate
        """;
    }

    private static async Task WriteTextAsync(
        ZipArchive archive, string path, string content, CancellationToken cancellationToken) =>
        await WriteEntryAsync(archive, path, new UTF8Encoding(false).GetBytes(content), cancellationToken);

    private static async Task WriteEntryAsync(
        ZipArchive archive, string path, byte[] content, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await stream.WriteAsync(content, cancellationToken);
    }

    private static async Task WriteEmbeddedAsync(
        ZipArchive archive, string path, string resourceName, CancellationToken cancellationToken)
    {
        await using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' is missing from the build.");

        using var buffer = new MemoryStream();
        await resource.CopyToAsync(buffer, cancellationToken);
        await WriteEntryAsync(archive, path, buffer.ToArray(), cancellationToken);
    }

    [LoggerMessage(
        EventId = 5000,
        Level = LogLevel.Information,
        Message = "Built evidence package {PackageId} for snapshot {SnapshotId}.")]
    private partial void LogPackageBuilt(Guid snapshotId, Guid packageId);

    [LoggerMessage(
        EventId = 5001,
        Level = LogLevel.Warning,
        Message = "No timestamp authority certificate chain is configured (Evidence:CaChainPemPath), so "
                  + "evidence packages cannot be verified offline. The recipient will have to source the "
                  + "authority's certificates themselves.")]
    private partial void LogMissingCaChain();
}
