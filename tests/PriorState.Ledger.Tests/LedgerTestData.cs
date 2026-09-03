using PriorState.Domain.Entities;
using PriorState.Domain.ValueObjects;
using PriorState.Ledger;

namespace PriorState.Ledger.Tests;

/// <summary>
/// Fixed test data. Values here are deliberately hard-coded rather than randomised: these tests
/// exist to catch an accidental change to the canonical form, and a generator that drifts along
/// with the code under test would not catch anything.
/// </summary>
internal static class LedgerTestData
{
    // Declared before Profile: static property initialisers run in declaration order, and Profile
    // reads this one.
    public static CaptureConditions Conditions { get; } = new()
    {
        UserAgent = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 Chrome/140.0.0.0 Safari/537.36",
        ViewportWidth = 1920,
        ViewportHeight = 1080,
        AuthenticatedSession = false,
        AdBlockerActive = false,
        CookieBanner = CookieBannerHandling.LeftAsIs,
        JavaScriptSettleMs = 5000,
        ChromiumVersion = "140.0.7259.68",
        CrawlerVersion = "1.7.1",
    };

    public static CaptureProfileVersion Profile { get; } = new()
    {
        Id = Guid.Parse("0192f000-0000-7000-8000-000000000001"),
        Name = "DE-Standard",
        Version = 1,
        Rationale = "Baseline profile for German-language sites.",
        Conditions = Conditions,
    };

    public static Snapshot Snapshot(
        long sequence = 1,
        Sha256Hash? previousHash = null,
        string url = "https://example.com/prices",
        string waczHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")
    {
        var snapshot = new Snapshot
        {
            Id = Guid.Parse("0192f000-0000-7000-8000-00000000000a"),
            Url = url,
            FinalUrl = null,
            CapturedAtUtc = new DateTimeOffset(2026, 9, 3, 14, 30, 0, TimeSpan.Zero),
            PayloadSha256 = Sha256Hash.Parse(waczHash),
            PayloadObjectKey = "projects/demo/2026/09/03/prices.wacz",
            PayloadSizeBytes = 1_048_576,
            PayloadMediaType = "application/wacz",
            CanonicalFormVersion = CanonicalSnapshotForm.Version1,
            CaptureProfileVersionId = Profile.Id,
            CaptureProfileVersion = Profile,
            Conditions = Conditions,
            ChainSequence = sequence,
            PreviousHash = previousHash ?? Sha256Hash.Genesis,
            EntryHash = Sha256Hash.Genesis,
            StorageWorm = WormSupport.Unsupported,
        };

        snapshot.EntryHash = CanonicalSnapshotForm.ComputeEntryHash(snapshot);
        return snapshot;
    }

    // A fresh instance per access: one of the tests mutates the configuration to prove the
    // entry hash moves with it, and a shared instance would leak that into its neighbours.
    public static PluginBindingVersion Binding => new()
    {
        Id = Guid.Parse("0192f000-0000-7000-8000-000000000002"),
        ProjectId = Guid.Parse("0192f000-0000-7000-8000-000000000003"),
        PluginId = "http-json",
        Name = "erp-prices",
        Version = 3,
        ConfigurationJson = """{"method":"GET","url":"https://erp.example.com/api/prices"}""",
        SecretRef = "PS_SECRET_ERP_TOKEN",
        Rationale = "Prices are quoted on the shop page and have to be archived with it.",
        Required = false,
        CreatedAt = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero),
    };

    /// <summary>A snapshot produced by a capture plugin, rendered under the v2 canonical form.</summary>
    public static Snapshot PluginSnapshot(
        long sequence = 1,
        Sha256Hash? previousHash = null,
        string url = "https://erp.example.com/api/prices")
    {
        var snapshot = new Snapshot
        {
            Id = Guid.Parse("0192f000-0000-7000-8000-00000000000b"),
            Url = url,
            FinalUrl = null,
            CapturedAtUtc = new DateTimeOffset(2026, 9, 3, 14, 30, 0, TimeSpan.Zero),
            PayloadSha256 = Sha256Hash.Parse("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08"),
            PayloadObjectKey = "projects/demo/runs/abc/plugins/http-json/prices.json",
            PayloadSizeBytes = 2048,
            PayloadMediaType = "application/json",
            CanonicalFormVersion = CanonicalSnapshotForm.Version2,
            CaptureProfileVersionId = Profile.Id,
            CaptureProfileVersion = Profile,
            Conditions = null,
            PluginBindingVersionId = Binding.Id,
            PluginBindingVersion = Binding,
            PluginVersion = "1.4.2",
            ChainSequence = sequence,
            PreviousHash = previousHash ?? Sha256Hash.Genesis,
            EntryHash = Sha256Hash.Genesis,
            StorageWorm = WormSupport.Unsupported,
        };

        snapshot.EntryHash = CanonicalSnapshotForm.ComputeEntryHash(snapshot);
        return snapshot;
    }

    /// <summary>Builds a correctly linked chain of the given length.</summary>
    public static List<Snapshot> Chain(int length)
    {
        var chain = new List<Snapshot>(length);
        var previous = Sha256Hash.Genesis;

        for (var i = 1; i <= length; i++)
        {
            var snapshot = Snapshot(sequence: i, previousHash: previous, url: $"https://example.com/page-{i}");
            snapshot.Id = Guid.CreateVersion7();
            chain.Add(snapshot);
            previous = snapshot.EntryHash;
        }

        return chain;
    }
}
