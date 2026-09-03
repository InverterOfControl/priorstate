using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PriorState.Domain.Entities;
using PriorState.Domain.ValueObjects;

namespace PriorState.Evidence;

/// <summary>
/// Renders the protocol template to PDF with the same Chromium that performs captures.
///
/// Chosen over a PDF library on purpose. A library would add a dependency with its own licence
/// terms to a project whose whole argument is that anyone may inspect and re-run the process;
/// the browser is already here, is already the thing that rendered the page being attested to,
/// and produces a PDF from HTML that a reviewer can also open in their own browser to compare.
/// </summary>
public sealed partial class ChromiumProtocolRenderer : IProtocolRenderer, IDisposable
{
    private readonly DockerClient _docker;
    private readonly EvidenceOptions _options;
    private readonly ILogger<ChromiumProtocolRenderer> _logger;

    public ChromiumProtocolRenderer(IOptions<EvidenceOptions> options, ILogger<ChromiumProtocolRenderer> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _logger = logger;
        _docker = new DockerClientConfiguration(new Uri(_options.DockerEndpoint)).CreateClient();
    }

    public async Task<byte[]> RenderAsync(
        EvidencePackageRequest request,
        Guid packageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var job = packageId.ToString("n");
        var hostDirectory = Path.Combine(_options.WorkDirectory, job);
        Directory.CreateDirectory(hostDirectory);

        try
        {
            var html = Substitute(await LoadTemplateAsync(cancellationToken), request, packageId);
            var htmlPath = Path.Combine(hostDirectory, "protocol.html");
            await File.WriteAllTextAsync(htmlPath, html, new UTF8Encoding(false), cancellationToken);

            await RunChromiumAsync(job, cancellationToken);

            var pdfPath = Path.Combine(hostDirectory, "protocol.pdf");
            if (!File.Exists(pdfPath))
            {
                throw new InvalidOperationException(
                    "Chromium did not produce protocol.pdf. The evidence package cannot be completed.");
            }

            return await File.ReadAllBytesAsync(pdfPath, cancellationToken);
        }
        finally
        {
            try
            {
                Directory.Delete(hostDirectory, recursive: true);
            }
            catch (IOException ex)
            {
                LogWorkDirectoryCleanupFailed(hostDirectory, ex.Message);
            }
        }
    }

    private async Task RunChromiumAsync(string job, CancellationToken cancellationToken)
    {
        var container = await _docker.Containers.CreateContainerAsync(
            new CreateContainerParameters
            {
                Image = _options.RendererImage,
                Entrypoint = ["/bin/sh", "-c"],
                Cmd =
                [
                    "chromium-browser --headless --disable-gpu --no-sandbox "
                    + "--run-all-compositor-stages-before-draw --virtual-time-budget=4000 "
                    + "--no-pdf-header-footer --print-to-pdf=/render/protocol.pdf "
                    + "file:///render/protocol.html "
                    + "|| chromium --headless --disable-gpu --no-sandbox "
                    + "--run-all-compositor-stages-before-draw --virtual-time-budget=4000 "
                    + "--no-pdf-header-footer --print-to-pdf=/render/protocol.pdf "
                    + "file:///render/protocol.html",
                ],
                HostConfig = new HostConfig
                {
                    Binds = [$"{Path.Combine(_options.HostWorkDirectory, job)}:/render"],
                    // The protocol is generated from our own template and our own data. It has no
                    // reason to reach the network, so it does not get to.
                    NetworkMode = "none",
                    AutoRemove = false,
                },
            },
            cancellationToken);

        try
        {
            await _docker.Containers.StartContainerAsync(container.ID, new ContainerStartParameters(), cancellationToken);
            var wait = await _docker.Containers.WaitContainerAsync(container.ID, cancellationToken);

            if (wait.StatusCode != 0)
            {
                LogRendererFailed((int)wait.StatusCode);
            }
        }
        finally
        {
            try
            {
                await _docker.Containers.RemoveContainerAsync(
                    container.ID, new ContainerRemoveParameters { Force = true }, CancellationToken.None);
            }
            catch (DockerApiException)
            {
                // Cleanup is best effort; a stray container must not fail an export.
            }
        }
    }

    private async Task<string> LoadTemplateAsync(CancellationToken cancellationToken)
    {
        if (_options.ProtocolTemplatePath is { Length: > 0 } path && File.Exists(path))
        {
            return await File.ReadAllTextAsync(path, cancellationToken);
        }

        await using var resource = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("PriorState.Evidence.Resources.protocol.html")
            ?? throw new InvalidOperationException("The protocol template is missing from the build.");

        using var reader = new StreamReader(resource, Encoding.UTF8);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private string Substitute(string template, EvidencePackageRequest request, Guid packageId)
    {
        var s = request.Snapshot;
        var a = request.Anchor;
        var c = s.Conditions;

        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SnapshotId"] = s.Id.ToString(),
            ["PackageId"] = packageId.ToString(),
            ["GeneratedAt"] = Ledger.CanonicalSnapshotForm.FormatTimestamp(DateTimeOffset.UtcNow),
            ["ToolVersion"] = _options.ToolVersion,
            ["CanonicalFormVersion"] = Ledger.CanonicalSnapshotForm.Version1,
            ["Url"] = s.Url,
            ["FinalUrl"] = s.FinalUrl ?? "— (keine Weiterleitung)",
            ["CapturedAtUtc"] = Ledger.CanonicalSnapshotForm.FormatTimestamp(s.CapturedAtUtc),
            ["WaczSizeBytes"] = FormatGermanNumber(s.WaczSizeBytes),
            ["ProfileDesignation"] = s.CaptureProfileVersion?.Designation ?? "unbekannt",
            ["UserAgent"] = c.UserAgent,
            ["Viewport"] = $"{c.ViewportWidth} × {c.ViewportHeight}",
            ["AuthenticatedSession"] = c.AuthenticatedSession ? "ja" : "nein",
            ["AdBlockerActive"] = c.AdBlockerActive ? "ja" : "nein",
            ["CookieBanner"] = c.CookieBanner == CookieBannerHandling.Dismissed
                ? "durch den Crawler geschlossen"
                : "unverändert wie ausgeliefert",
            ["JavaScriptSettleMs"] = c.JavaScriptSettleMs.ToString(CultureInfo.InvariantCulture),
            ["ChromiumVersion"] = c.ChromiumVersion,
            ["CrawlerVersion"] = c.CrawlerVersion,
            ["ChainSequence"] = s.ChainSequence.ToString(CultureInfo.InvariantCulture),
            ["WaczSha256"] = s.WaczSha256.Value,
            ["PreviousHash"] = s.PreviousHash.Value,
            ["EntryHash"] = s.EntryHash.Value,
            ["MerkleRoot"] = a.MerkleRoot.Value,
            ["TsaUrl"] = a.TsaUrl,
            ["TsaGeneralizedTime"] = Ledger.CanonicalSnapshotForm.FormatTimestamp(a.TsaGeneralizedTime),
            ["TsaQualified"] = a.QualifiedProvider ? "ja" : "nein",
            ["TsaWarning"] = a.QualifiedProvider ? string.Empty : UnqualifiedTsaWarning,
            ["StorageWorm"] = DescribeWorm(s.StorageWorm),
            ["WormRetainUntil"] = s.WormRetainUntil is { } until
                ? Ledger.CanonicalSnapshotForm.FormatTimestamp(until)
                : "— (keine Speichersperre gesetzt)",
        };

        var rendered = new StringBuilder(template);
        foreach (var (key, value) in values)
        {
            // TsaWarning is markup we generated ourselves; everything else is data and is escaped.
            rendered.Replace(
                "{{" + key + "}}",
                key == "TsaWarning" ? value : WebUtility.HtmlEncode(value));
        }

        return rendered.ToString();
    }

    /// <summary>
    /// Printed on the protocol whenever the timestamp came from a non-qualified authority. It is
    /// not hidden behind a configuration flag: a reader who is handed one of these documents
    /// needs to know what it is worth before they rely on it.
    /// </summary>
    private const string UnqualifiedTsaWarning =
        """
        <div class="notice warn">
          <p>
            <strong>Hinweis zum Zeitstempeldienst.</strong> Der verwendete Dienst ist nicht als
            qualifizierter Vertrauensdiensteanbieter nach eIDAS gekennzeichnet. Der Zeitstempel ist
            technisch gültig und mit dem beiliegenden Skript nachprüfbar, erfüllt aber nicht die
            Anforderungen an einen qualifizierten elektronischen Zeitstempel.
          </p>
          <p>
            Für eine beabsichtigte Verwendung in einer rechtlichen Auseinandersetzung ist ein
            qualifizierter Anbieter zu konfigurieren. Bereits erfasste Stände lassen sich
            nachträglich nicht auf einen anderen Zeitstempeldienst umstellen.
          </p>
        </div>
        """;

    /// <summary>
    /// German thousands grouping without pulling in ICU. The project builds with
    /// InvariantGlobalization, so asking for a de-DE culture at runtime would throw; every other
    /// value on the protocol is an ISO-8601 UTC timestamp or a hex digest, which need no culture
    /// at all. This is the one place a locale would otherwise be required.
    /// </summary>
    private static string FormatGermanNumber(long value) =>
        value.ToString("N0", CultureInfo.InvariantCulture).Replace(',', '.');

    private static string DescribeWorm(WormSupport worm) => worm switch
    {
        WormSupport.Enforced =>
            "durchgesetzt — der Speicher hat einen Löschversuch während der Sperrfrist nachweislich abgewiesen",
        WormSupport.ApiPresentUnverified =>
            "angefordert, aber nicht bestätigt — der Speicher nimmt Sperrfristen entgegen, ihre Durchsetzung "
            + "konnte nicht überprüft werden",
        WormSupport.Unsupported =>
            "nicht verfügbar — der Speicher kennt keine Sperrfristen; der Nachweis beruht auf Hash-Kette und "
            + "Zeitstempel",
        _ => worm.ToString(),
    };

    public void Dispose() => _docker.Dispose();

    [LoggerMessage(
        EventId = 5100,
        Level = LogLevel.Error,
        Message = "The protocol renderer exited with code {ExitCode}.")]
    private partial void LogRendererFailed(int exitCode);

    [LoggerMessage(
        EventId = 5101,
        Level = LogLevel.Warning,
        Message = "Could not clean up the render directory {Directory}: {Reason}")]
    private partial void LogWorkDirectoryCleanupFailed(string directory, string reason);
}
