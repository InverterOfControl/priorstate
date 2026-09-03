using System.Globalization;
using System.Text;

namespace PriorState.Ledger;

/// <summary>
/// The line-oriented encoding shared by every canonical form in PriorState.
///
/// Extracted so that a second form cannot quietly escape differently from the first. The rules are
/// the ones documented on <see cref="CanonicalSnapshotForm"/>: UTF-8, LF, one trailing newline, no
/// BOM; in values, backslash becomes \\, LF becomes \n, CR becomes \r, and nothing else is
/// escaped, so a value can never introduce a line break and forge a record.
/// </summary>
internal static class CanonicalText
{
    /// <summary>Second precision, always UTC, always Z-suffixed.</summary>
    public const string TimestampFormat = "yyyy-MM-ddTHH:mm:ssZ";

    public static void Field(StringBuilder builder, string key, string value)
    {
        builder.Append(key).Append('=').Append(Escape(value)).Append('\n');
    }

    public static string FormatTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture);

    public static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Bool(bool value) => value ? "true" : "false";

    public static byte[] ToUtf8(StringBuilder builder) =>
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(builder.ToString());

    public static string Escape(string value)
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
}
