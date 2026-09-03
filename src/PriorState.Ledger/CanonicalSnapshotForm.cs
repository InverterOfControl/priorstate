using System.Text;
using PriorState.Domain.Entities;
using PriorState.Domain.ValueObjects;

namespace PriorState.Ledger;

/// <summary>
/// Renders a snapshot into the exact byte sequence that gets hashed into the chain.
///
/// This format is a compatibility contract with every evidence package ever exported. The
/// verification script an opposing party runs rebuilds these bytes with nothing but shell
/// builtins and recomputes the hash; if the two disagree, the snapshot is treated as tampered.
/// So:
///
///   - Never change, reorder or remove a field in an existing version.
///   - A new field means a new version constant and a new branch here. Old snapshots keep being
///     rendered under the version they were written with, forever.
///   - The format is deliberately line-oriented rather than JSON. Canonical JSON (RFC 8785) is
///     painful to reproduce in a 150-line shell script, and the script has to stay readable
///     enough that a court-appointed expert will actually read it.
///
/// There are two versions, and which one applies is a property of the snapshot rather than of the
/// code, so that adding a v3 later cannot retroactively change what an existing entry hashes to:
///
///   v1  a page capture. The payload is a WACZ archive, and the browser conditions it ran under
///       are part of the record.
///   v2  a capture plugin's output. The payload is whatever the plugin returned, and the fields
///       describing a browser are absent rather than invented: an API call has no viewport, and
///       stating one would be a false claim in the one document written to be read adversarially.
///       In their place it commits to which plugin ran, at which observed version, under which
///       configuration.
///
/// Encoding: UTF-8, LF line endings, one trailing newline, no BOM.
/// Escaping: in values, backslash becomes \\, LF becomes \n, CR becomes \r. Nothing else is
/// escaped, so field values cannot introduce a line break and forge a record.
/// </summary>
public static class CanonicalSnapshotForm
{
    /// <summary>Version marker for a page capture, and the first line of its canonical form.</summary>
    public const string Version1 = "priorstate-snapshot-v1";

    /// <summary>Version marker for a capture plugin's output.</summary>
    public const string Version2 = "priorstate-snapshot-v2";

    /// <summary>The timestamp format. Second precision, always UTC, always Z-suffixed.</summary>
    public const string TimestampFormat = CanonicalText.TimestampFormat;

    public static byte[] Render(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return snapshot.CanonicalFormVersion switch
        {
            Version1 => RenderVersion1(snapshot),
            Version2 => RenderVersion2(snapshot),
            _ => throw new InvalidOperationException(
                $"Snapshot {snapshot.Id} names canonical form '{snapshot.CanonicalFormVersion}', which this "
                + "build cannot render. A package exported by a newer PriorState needs a newer PriorState "
                + "to re-verify."),
        };
    }

    private static byte[] RenderVersion1(Snapshot snapshot)
    {
        var profile = snapshot.CaptureProfileVersion
            ?? throw new InvalidOperationException(
                $"Snapshot {snapshot.Id} must have its CaptureProfileVersion loaded before hashing.");

        var c = snapshot.Conditions
            ?? throw new InvalidOperationException(
                $"Snapshot {snapshot.Id} is a v1 page capture and must have its Conditions set before hashing.");

        var builder = new StringBuilder();

        builder.Append(Version1).Append('\n');
        Field(builder, "sequence", CanonicalText.Number(snapshot.ChainSequence));
        Field(builder, "prev", snapshot.PreviousHash.Value);
        Field(builder, "url", snapshot.Url);
        Field(builder, "final_url", snapshot.FinalUrl ?? string.Empty);
        Field(builder, "captured_at", FormatTimestamp(snapshot.CapturedAtUtc));
        Field(builder, "wacz_sha256", snapshot.PayloadSha256.Value);
        Field(builder, "wacz_size", CanonicalText.Number(snapshot.PayloadSizeBytes));
        Field(builder, "profile", profile.Designation);
        Field(builder, "user_agent", c.UserAgent);
        Field(builder, "viewport", $"{c.ViewportWidth}x{c.ViewportHeight}");
        Field(builder, "authenticated", Bool(c.AuthenticatedSession));
        Field(builder, "adblock", Bool(c.AdBlockerActive));
        Field(builder, "cookie_banner", CookieBanner(c.CookieBanner));
        Field(builder, "js_settle_ms", CanonicalText.Number(c.JavaScriptSettleMs));
        Field(builder, "chromium", c.ChromiumVersion);
        Field(builder, "crawler", c.CrawlerVersion);

        return CanonicalText.ToUtf8(builder);
    }

    private static byte[] RenderVersion2(Snapshot snapshot)
    {
        var profile = snapshot.CaptureProfileVersion
            ?? throw new InvalidOperationException(
                $"Snapshot {snapshot.Id} must have its CaptureProfileVersion loaded before hashing.");

        var binding = snapshot.PluginBindingVersion
            ?? throw new InvalidOperationException(
                $"Snapshot {snapshot.Id} is a v2 plugin snapshot and must have its PluginBindingVersion "
                + "loaded before hashing.");

        // Observed at execution time, from the assembly that ran. A snapshot reaching this point
        // without one did not come from the plugin runner, and the record would be asserting a
        // provenance that nothing actually checked.
        var pluginVersion = snapshot.PluginVersion
            ?? throw new InvalidOperationException(
                $"Snapshot {snapshot.Id} is a v2 plugin snapshot and must have its observed PluginVersion set.");

        var builder = new StringBuilder();

        builder.Append(Version2).Append('\n');
        Field(builder, "sequence", CanonicalText.Number(snapshot.ChainSequence));
        Field(builder, "prev", snapshot.PreviousHash.Value);
        Field(builder, "url", snapshot.Url);
        Field(builder, "final_url", snapshot.FinalUrl ?? string.Empty);
        Field(builder, "captured_at", FormatTimestamp(snapshot.CapturedAtUtc));
        Field(builder, "payload_sha256", snapshot.PayloadSha256.Value);
        Field(builder, "payload_size", CanonicalText.Number(snapshot.PayloadSizeBytes));
        Field(builder, "payload_media_type", snapshot.PayloadMediaType);
        Field(builder, "profile", profile.Designation);
        Field(builder, "plugin", binding.PluginId);
        Field(builder, "plugin_version", pluginVersion);
        Field(builder, "binding", binding.Designation);
        Field(builder, "binding_digest", CanonicalPluginBindingForm.ComputeDigest(binding).Value);

        return CanonicalText.ToUtf8(builder);
    }

    /// <summary>SHA-256 over the canonical form. This value is the link in the chain.</summary>
    public static Sha256Hash ComputeEntryHash(Snapshot snapshot)
    {
        var canonical = Render(snapshot);
        return Sha256Hash.FromBytes(System.Security.Cryptography.SHA256.HashData(canonical));
    }

    /// <summary>UTC, truncated to the second. Sub-second precision is not carried into the chain.</summary>
    public static string FormatTimestamp(DateTimeOffset value) => CanonicalText.FormatTimestamp(value);

    private static void Field(StringBuilder builder, string key, string value) =>
        CanonicalText.Field(builder, key, value);

    private static string Bool(bool value) => CanonicalText.Bool(value);

    private static string CookieBanner(CookieBannerHandling handling) => handling switch
    {
        CookieBannerHandling.LeftAsIs => "left_as_is",
        CookieBannerHandling.Dismissed => "dismissed",
        _ => throw new ArgumentOutOfRangeException(nameof(handling), handling, "Unmapped cookie banner handling."),
    };
}
