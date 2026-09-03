using PriorState.Domain.Entities;
using PriorState.Domain.ValueObjects;
using PriorState.Evidence;
using PriorState.Ledger;

namespace PriorState.Evidence.Tests;

/// <summary>
/// The protocol says different things about a page capture and about a plugin snapshot, and both
/// documents are read adversarially. These tests pin the claims that must be present and, just as
/// importantly, the ones that must not: a plugin protocol that quietly listed a viewport would be
/// asserting a fact nobody observed.
/// </summary>
public sealed class ProtocolBlocksTests
{
    [Fact]
    public void PageCapture_StatesTheLimitsOfWhatItCertifies()
    {
        var blocks = ProtocolBlocks.For(PageCaptureRequest());

        Assert.Contains("Grenzen dieses Protokolls", blocks["ScopeNotice"], StringComparison.Ordinal);
        Assert.Contains("vollständig oder repräsentativ", blocks["ScopeNotice"], StringComparison.Ordinal);
    }

    [Fact]
    public void PluginSnapshot_StatesThatOnlyReceiptIsCertifiedAndNotCorrectness()
    {
        // The one claim a plugin snapshot must never be read as making. PriorState saw the bytes
        // arrive; it has no way to know whether the upstream system was telling the truth.
        var blocks = ProtocolBlocks.For(PluginRequest());

        Assert.Contains("Grenzen dieses Protokolls", blocks["ScopeNotice"], StringComparison.Ordinal);
        Assert.Contains("nicht die Richtigkeit", blocks["ScopeNotice"], StringComparison.Ordinal);
    }

    [Fact]
    public void PageCapture_RecordsTheBrowserConditions()
    {
        var block = ProtocolBlocks.For(PageCaptureRequest())["CaptureContextBlock"];

        Assert.Contains("Chromium-Version", block, StringComparison.Ordinal);
        Assert.Contains("131.0.6778.85", block, StringComparison.Ordinal);
        Assert.Contains("Darstellungsfläche", block, StringComparison.Ordinal);
    }

    [Fact]
    public void PluginSnapshot_OmitsBrowserConditionsRatherThanInventingThem()
    {
        var block = ProtocolBlocks.For(PluginRequest())["CaptureContextBlock"];

        // The prose does mention these fields, to say that they are deliberately absent. What must
        // not appear is a table row asserting a value for any of them.
        Assert.DoesNotContain("<th>Chromium-Version</th>", block, StringComparison.Ordinal);
        Assert.DoesNotContain("<th>Darstellungsfläche</th>", block, StringComparison.Ordinal);
        Assert.DoesNotContain("<th>User-Agent</th>", block, StringComparison.Ordinal);

        // And no value carried over from the run's capture profile, which is loaded on this
        // snapshot and would be the easy thing to reach for.
        Assert.DoesNotContain("131.0.6778.85", block, StringComparison.Ordinal);
        Assert.DoesNotContain("1920", block, StringComparison.Ordinal);
        Assert.DoesNotContain("Mozilla/5.0", block, StringComparison.Ordinal);
    }

    [Fact]
    public void PluginSnapshot_NamesThePluginItsVersionAndItsConfiguration()
    {
        var request = PluginRequest();
        var block = ProtocolBlocks.For(request)["CaptureContextBlock"];
        var expectedDigest = CanonicalPluginBindingForm.ComputeDigest(request.Snapshot.PluginBindingVersion!).Value;

        Assert.Contains("http-json", block, StringComparison.Ordinal);
        Assert.Contains("erp-prices v3", block, StringComparison.Ordinal);
        Assert.Contains("1.4.2", block, StringComparison.Ordinal);
        Assert.Contains(expectedDigest, block, StringComparison.Ordinal);
    }

    [Fact]
    public void PluginSnapshot_NamesTheSecretVariableButNeverItsValue()
    {
        var block = ProtocolBlocks.For(PluginRequest())["CaptureContextBlock"];

        Assert.Contains("PS_SECRET_ERP_TOKEN", block, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", block, StringComparison.Ordinal);
    }

    [Fact]
    public void PayloadRows_NameTheFileThatIsActuallyShipped()
    {
        var pageCapture = ProtocolBlocks.For(PageCaptureRequest());
        var plugin = ProtocolBlocks.For(PluginRequest());

        Assert.Contains("snapshot.wacz", pageCapture["PayloadSummaryRow"], StringComparison.Ordinal);
        Assert.Contains("payload.json", plugin["PayloadSummaryRow"], StringComparison.Ordinal);
        Assert.Contains("application/json", plugin["PayloadSummaryRow"], StringComparison.Ordinal);
    }

    private static EvidencePackageRequest PageCaptureRequest() => Request(PageCaptureSnapshot());

    private static EvidencePackageRequest PluginRequest() => Request(PluginSnapshot());

    private static EvidencePackageRequest Request(Snapshot snapshot) => new()
    {
        Snapshot = snapshot,
        Anchor = Anchor(),
        LeafIndex = 0,
        AuditPath = [],
    };

    private static Snapshot PageCaptureSnapshot() => new()
    {
        Url = "https://example.test/preise",
        CapturedAtUtc = new DateTimeOffset(2026, 9, 3, 10, 0, 0, TimeSpan.Zero),
        PayloadSha256 = Sha256Hash.Parse(new string('a', 64)),
        PayloadObjectKey = "projects/x/runs/y/capture.wacz",
        PayloadSizeBytes = 1_048_576,
        PayloadMediaType = "application/wacz",
        CanonicalFormVersion = CanonicalSnapshotForm.Version1,
        CaptureProfileVersion = Profile(),
        Conditions = new CaptureConditions
        {
            UserAgent = "Mozilla/5.0",
            ViewportWidth = 1920,
            ViewportHeight = 1080,
            AuthenticatedSession = false,
            AdBlockerActive = false,
            CookieBanner = CookieBannerHandling.LeftAsIs,
            JavaScriptSettleMs = 2000,
            ChromiumVersion = "131.0.6778.85",
            CrawlerVersion = "1.7.1",
        },
        ChainSequence = 1,
        PreviousHash = Sha256Hash.Genesis,
        EntryHash = Sha256Hash.Parse(new string('b', 64)),
        StorageWorm = WormSupport.Enforced,
    };

    private static Snapshot PluginSnapshot() => new()
    {
        Url = "https://erp.internal.test/api/prices",
        CapturedAtUtc = new DateTimeOffset(2026, 9, 3, 10, 0, 0, TimeSpan.Zero),
        PayloadSha256 = Sha256Hash.Parse(new string('c', 64)),
        PayloadObjectKey = "projects/x/runs/y/plugins/http-json/prices.json",
        PayloadSizeBytes = 4096,
        PayloadMediaType = "application/json",
        CanonicalFormVersion = CanonicalSnapshotForm.Version2,
        CaptureProfileVersion = Profile(),
        Conditions = null,
        PluginVersion = "1.4.2",
        PluginBindingVersion = new PluginBindingVersion
        {
            PluginId = "http-json",
            Name = "erp-prices",
            Version = 3,
            ConfigurationJson = "{\"url\":\"https://erp.internal.test/api/prices\"}",
            SecretRef = "PS_SECRET_ERP_TOKEN",
            Rationale = "Prices have to be archived alongside the shop page.",
            Required = false,
            CreatedAt = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero),
        },
        ChainSequence = 2,
        PreviousHash = Sha256Hash.Parse(new string('b', 64)),
        EntryHash = Sha256Hash.Parse(new string('d', 64)),
        StorageWorm = WormSupport.Enforced,
    };

    private static CaptureProfileVersion Profile() => new()
    {
        Name = "DE-Standard",
        Version = 1,
        Rationale = "Baseline.",
        Conditions = new CaptureConditions
        {
            UserAgent = "Mozilla/5.0",
            ViewportWidth = 1920,
            ViewportHeight = 1080,
            AuthenticatedSession = false,
            AdBlockerActive = false,
            CookieBanner = CookieBannerHandling.LeftAsIs,
            JavaScriptSettleMs = 2000,
            ChromiumVersion = "131.0.6778.85",
            CrawlerVersion = "1.7.1",
        },
    };

    private static TimestampAnchor Anchor() => new()
    {
        CoversFromUtc = new DateTimeOffset(2026, 9, 3, 0, 0, 0, TimeSpan.Zero),
        CoversUntilUtc = new DateTimeOffset(2026, 9, 4, 0, 0, 0, TimeSpan.Zero),
        FirstChainSequence = 1,
        LastChainSequence = 2,
        MerkleRoot = Sha256Hash.Parse(new string('e', 64)),
        TimestampToken = [1, 2, 3],
        TsaUrl = "https://tsa.example.test",
        TsaGeneralizedTime = new DateTimeOffset(2026, 9, 4, 1, 0, 0, TimeSpan.Zero),
        QualifiedProvider = true,
    };
}
