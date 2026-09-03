using System.Globalization;
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
/// Encoding: UTF-8, LF line endings, one trailing newline, no BOM.
/// Escaping: in values, backslash becomes \\, LF becomes \n, CR becomes \r. Nothing else is
/// escaped, so field values cannot introduce a line break and forge a record.
/// </summary>
public static class CanonicalSnapshotForm
{
    /// <summary>Version marker, and the first line of every canonical form.</summary>
    public const string Version1 = "priorstate-snapshot-v1";

    /// <summary>The timestamp format. Second precision, always UTC, always Z-suffixed.</summary>
    public const string TimestampFormat = "yyyy-MM-ddTHH:mm:ssZ";

    public static byte[] Render(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var profile = snapshot.CaptureProfileVersion
            ?? throw new InvalidOperationException(
                $"Snapshot {snapshot.Id} must have its CaptureProfileVersion loaded before hashing.");

        var c = snapshot.Conditions;
        var builder = new StringBuilder();

        builder.Append(Version1).Append('\n');
        Field(builder, "sequence", snapshot.ChainSequence.ToString(CultureInfo.InvariantCulture));
        Field(builder, "prev", snapshot.PreviousHash.Value);
        Field(builder, "url", snapshot.Url);
        Field(builder, "final_url", snapshot.FinalUrl ?? string.Empty);
        Field(builder, "captured_at", FormatTimestamp(snapshot.CapturedAtUtc));
        Field(builder, "wacz_sha256", snapshot.WaczSha256.Value);
        Field(builder, "wacz_size", snapshot.WaczSizeBytes.ToString(CultureInfo.InvariantCulture));
        Field(builder, "profile", profile.Designation);
        Field(builder, "user_agent", c.UserAgent);
        Field(builder, "viewport", $"{c.ViewportWidth}x{c.ViewportHeight}");
        Field(builder, "authenticated", Bool(c.AuthenticatedSession));
        Field(builder, "adblock", Bool(c.AdBlockerActive));
        Field(builder, "cookie_banner", CookieBanner(c.CookieBanner));
        Field(builder, "js_settle_ms", c.JavaScriptSettleMs.ToString(CultureInfo.InvariantCulture));
        Field(builder, "chromium", c.ChromiumVersion);
        Field(builder, "crawler", c.CrawlerVersion);

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(builder.ToString());
    }

    /// <summary>SHA-256 over the canonical form. This value is the link in the chain.</summary>
    public static Sha256Hash ComputeEntryHash(Snapshot snapshot)
    {
        var canonical = Render(snapshot);
        return Sha256Hash.FromBytes(System.Security.Cryptography.SHA256.HashData(canonical));
    }

    /// <summary>UTC, truncated to the second. Sub-second precision is not carried into the chain.</summary>
    public static string FormatTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture);

    private static void Field(StringBuilder builder, string key, string value)
    {
        builder.Append(key).Append('=').Append(Escape(value)).Append('\n');
    }

    private static string Escape(string value)
    {
        if (!value.AsSpan().ContainsAny('\\', '\n', '\r'))
        {
            return value;
        }

        var escaped = new StringBuilder(value.Length + 8);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\\': escaped.Append("\\\\"); break;
                case '\n': escaped.Append("\\n"); break;
                case '\r': escaped.Append("\\r"); break;
                default: escaped.Append(ch); break;
            }
        }

        return escaped.ToString();
    }

    private static string Bool(bool value) => value ? "true" : "false";

    private static string CookieBanner(CookieBannerHandling handling) => handling switch
    {
        CookieBannerHandling.LeftAsIs => "left_as_is",
        CookieBannerHandling.Dismissed => "dismissed",
        _ => throw new ArgumentOutOfRangeException(nameof(handling), handling, "Unmapped cookie banner handling."),
    };
}
