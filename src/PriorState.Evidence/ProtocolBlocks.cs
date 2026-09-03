using System.Globalization;
using PriorState.Domain.Entities;
using PriorState.Domain.ValueObjects;
using PriorState.Ledger;

namespace PriorState.Evidence;

/// <summary>
/// The parts of the protocol that differ between a page capture and a plugin snapshot.
///
/// One template, two sets of blocks, rather than two templates: the stylesheet and the structure
/// of the document are the same argument in both cases, and a duplicated template would drift.
/// The blocks are markup rather than data and are injected unescaped, which is the same escape
/// hatch the unqualified-TSA warning already uses; every value interpolated into them is escaped
/// individually here.
/// </summary>
internal static class ProtocolBlocks
{
    public static Dictionary<string, string> For(EvidencePackageRequest request)
    {
        var snapshot = request.Snapshot;

        return string.Equals(snapshot.CanonicalFormVersion, CanonicalSnapshotForm.Version2, StringComparison.Ordinal)
            ? Plugin(request)
            : PageCapture(request);
    }

    private static Dictionary<string, string> PageCapture(EvidencePackageRequest request)
    {
        var s = request.Snapshot;
        var c = s.Conditions
            ?? throw new InvalidOperationException(
                $"Snapshot {s.Id} is a page capture and must have its Conditions loaded before rendering.");

        var cookieBanner = c.CookieBanner == CookieBannerHandling.Dismissed
            ? "durch den Crawler geschlossen"
            : "unverändert wie ausgeliefert";

        var conditions = string.Concat(
            "<h2>Erfassungsbedingungen</h2>\n<table>\n",
            Row("Erfassungsprofil", s.CaptureProfileVersion?.Designation ?? "unbekannt"),
            Row("User-Agent", c.UserAgent, hash: true),
            Row("Darstellungsfläche", $"{c.ViewportWidth} × {c.ViewportHeight} Pixel"),
            Row("Angemeldete Sitzung", c.AuthenticatedSession ? "ja" : "nein"),
            Row("Inhaltsblocker aktiv", c.AdBlockerActive ? "ja" : "nein"),
            Row("Cookie-Banner", cookieBanner),
            Row("Wartezeit nach Laden", c.JavaScriptSettleMs.ToString(CultureInfo.InvariantCulture) + " ms"),
            Row("Chromium-Version", c.ChromiumVersion),
            Row("Crawler-Version", c.CrawlerVersion),
            "</table>\n\n",
            Paragraph(
                "Die Erfassungsbedingungen sind nicht frei einstellbar, sondern durch ein benanntes und "
                + "versioniertes Profil festgelegt. Eine Änderung des Profils erzeugt eine neue Version und "
                + "wirkt ausschließlich für künftige Erfassungen; bereits erfasste Stände behalten das Profil, "
                + "unter dem sie aufgenommen wurden."));

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ProtocolSubtitle"] = "Nachweis über den Zustand einer Webseite zu einem bestimmten Zeitpunkt.",
            ["CaptureContextBlock"] = conditions,
            ["PayloadSummaryRow"] = Row(
                "Archivdatei",
                $"{EvidencePackageLayout.PageCapturePayload}, {FormatGermanNumber(s.PayloadSizeBytes)} Bytes"),
            ["PayloadHashRow"] = Row("Hash der Archivdatei", s.PayloadSha256.Value, hash: true),
            ["ScopeNotice"] =
                "<strong>Grenzen dieses Protokolls.</strong> Bescheinigt wird, dass die beiliegende Archivdatei "
                + "seit dem Erfassungszeitpunkt unverändert ist und zum bescheinigten Zeitpunkt bereits bestand. "
                + "Nicht bescheinigt wird, dass die Erfassung inhaltlich vollständig oder repräsentativ ist; "
                + "dies ist anhand der Archivdatei und der oben genannten Erfassungsbedingungen zu beurteilen.",
        };
    }

    private static Dictionary<string, string> Plugin(EvidencePackageRequest request)
    {
        var s = request.Snapshot;
        var binding = s.PluginBindingVersion
            ?? throw new InvalidOperationException(
                $"Snapshot {s.Id} is a plugin snapshot and must have its PluginBindingVersion loaded "
                + "before rendering.");

        var context = string.Concat(
            "<h2>Erfassung durch ein Zusatzmodul</h2>\n<table>\n",
            Row("Erfassungsprofil des Laufs", s.CaptureProfileVersion?.Designation ?? "unbekannt"),
            Row("Zusatzmodul", binding.PluginId),
            Row("Version des Moduls", s.PluginVersion ?? "unbekannt"),
            Row("Konfiguration", binding.Designation),
            Row("Hash der Konfiguration", CanonicalPluginBindingForm.ComputeDigest(binding).Value, hash: true),
            Row("Zugangsdaten aus", binding.SecretRef ?? "— (keine)"),
            "</table>\n\n",
            Paragraph(
                "Die Daten wurden nicht von einem Browser dargestellt, sondern von der oben genannten "
                + "Schnittstelle abgerufen. Angaben zu Darstellungsfläche, User-Agent oder Browserversion "
                + "entfallen daher; sie werden nicht ersatzweise angegeben."),
            Paragraph(
                "Die Konfiguration des Zusatzmoduls ist nicht frei einstellbar, sondern benannt und "
                + "versioniert. Eine Änderung erzeugt eine neue Version und wirkt ausschließlich für künftige "
                + "Läufe; bereits erfasste Stände behalten die Version, unter der sie aufgenommen wurden. Der "
                + "oben genannte Hash der Konfiguration ist Bestandteil des Eintrags-Hashes; die Konfiguration "
                + "selbst liegt dem Beweispaket als Datei " + EvidencePackageLayout.PluginConfiguration
                + " bei und lässt sich damit nachrechnen. Zugangsdaten sind nicht Bestandteil der "
                + "Konfiguration — aufgeführt ist nur der Name der Umgebungsvariable, aus der sie zur "
                + "Laufzeit gelesen wurden."));

        var payloadFile = EvidencePackageLayout.PayloadFileName(s);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ProtocolSubtitle"] =
                "Nachweis über die von einer Schnittstelle gelieferten Daten zu einem bestimmten Zeitpunkt.",
            ["CaptureContextBlock"] = context,
            ["PayloadSummaryRow"] = Row(
                "Nutzdaten",
                $"{payloadFile} ({s.PayloadMediaType}), {FormatGermanNumber(s.PayloadSizeBytes)} Bytes"),
            ["PayloadHashRow"] = Row("Hash der Nutzdaten", s.PayloadSha256.Value, hash: true),
            ["ScopeNotice"] =
                "<strong>Grenzen dieses Protokolls.</strong> Bescheinigt wird, dass die beiliegenden Nutzdaten "
                + "seit dem Erfassungszeitpunkt unverändert sind und zum bescheinigten Zeitpunkt bereits "
                + "bestanden. Nicht bescheinigt wird, dass die abgerufene Schnittstelle inhaltlich zutreffende "
                + "Angaben geliefert hat: bezeugt wird der Empfang, nicht die Richtigkeit.",
        };
    }

    /// <summary>Keys whose values are markup produced here and must not be escaped again.</summary>
    public static readonly IReadOnlySet<string> GeneratedMarkupKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "CaptureContextBlock", "PayloadSummaryRow", "PayloadHashRow", "ScopeNotice",
    };

    private static string Row(string label, string value, bool hash = false) =>
        "  <tr><th>" + Escape(label) + "</th><td"
        + (hash ? " class=\"hash\"" : string.Empty) + ">"
        + Escape(value) + "</td></tr>\n";

    private static string Paragraph(string text) => "<p>\n  " + Escape(text) + "\n</p>\n";

    /// <summary>
    /// Escapes the characters that matter in HTML and leaves everything else alone. The document
    /// is UTF-8 and the template already contains literal umlauts, so WebUtility's numeric
    /// entities for every non-ASCII character would only make the generated markup unreadable
    /// next to the template it is spliced into.
    /// </summary>
    private static string Escape(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);

    /// <summary>
    /// German thousands grouping without pulling in ICU, mirroring the renderer's own helper. The
    /// project builds with InvariantGlobalization, so asking for a de-DE culture would throw.
    /// </summary>
    private static string FormatGermanNumber(long value) =>
        value.ToString("N0", CultureInfo.InvariantCulture).Replace(',', '.');
}
