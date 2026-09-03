using System.Text;
using PriorState.Domain.ValueObjects;
using PriorState.Ledger;

namespace PriorState.Ledger.Tests;

/// <summary>
/// These pin the v2 snapshot form and the plugin binding form, on the same terms as the v1 tests:
/// if one fails because the format changed, that is not a test to update. Every evidence package
/// exported for a plugin snapshot would have become unverifiable. Add a new version instead.
/// </summary>
public sealed class CanonicalPluginFormTests
{
    [Fact]
    public void RenderVersion2_ProducesTheExactExpectedBytes()
    {
        var snapshot = LedgerTestData.PluginSnapshot();

        var rendered = Encoding.UTF8.GetString(CanonicalSnapshotForm.Render(snapshot));

        const string expected =
            "priorstate-snapshot-v2\n"
            + "sequence=1\n"
            + "prev=0000000000000000000000000000000000000000000000000000000000000000\n"
            + "url=https://erp.example.com/api/prices\n"
            + "final_url=\n"
            + "captured_at=2026-09-03T14:30:00Z\n"
            + "payload_sha256=9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08\n"
            + "payload_size=2048\n"
            + "payload_media_type=application/json\n"
            + "profile=DE-Standard v1\n"
            + "plugin=http-json\n"
            + "plugin_version=1.4.2\n"
            + "binding=erp-prices v3\n"
            + "binding_digest=56c946e0e9db65166f4eef0f32f714d0bfe94dd34f0d2e5addb65e7e4b6f41ca\n";

        Assert.Equal(expected, rendered);
    }

    [Fact]
    public void RenderBinding_ProducesTheExactExpectedBytes()
    {
        var rendered = Encoding.UTF8.GetString(CanonicalPluginBindingForm.Render(LedgerTestData.Binding));

        const string expected =
            "priorstate-plugin-binding-v1\n"
            + "plugin=http-json\n"
            + "name=erp-prices\n"
            + "version=3\n"
            + "secret_ref=PS_SECRET_ERP_TOKEN\n"
            + "required=false\n"
            + "created_at=2026-09-01T08:00:00Z\n"
            + "config_sha256=fbaad759812738f6695a660fa632871778e05b1c95c1e03f2f0e375371e16a3a\n";

        Assert.Equal(expected, rendered);
    }

    [Fact]
    public void RenderVersion2_OmitsEveryBrowserField()
    {
        // A plugin snapshot has no viewport and no Chromium. The absence is the point: an entry
        // that carried them would be asserting facts nobody observed.
        var rendered = Encoding.UTF8.GetString(CanonicalSnapshotForm.Render(LedgerTestData.PluginSnapshot()));

        foreach (var field in new[] { "user_agent=", "viewport=", "authenticated=", "adblock=", "cookie_banner=", "js_settle_ms=", "chromium=", "crawler=" })
        {
            Assert.DoesNotContain(field, rendered, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RenderVersion2_CommitsToTheConfigurationSoItCannotBeSwappedLater()
    {
        var snapshot = LedgerTestData.PluginSnapshot();
        var before = CanonicalSnapshotForm.ComputeEntryHash(snapshot);

        // Same binding designation, different endpoint. Without the digest in the entry, this
        // would be an undetectable change to what the snapshot claims about its own provenance.
        snapshot.PluginBindingVersion!.ConfigurationJson = """{"method":"GET","url":"https://elsewhere.example/"}""";

        Assert.NotEqual(before, CanonicalSnapshotForm.ComputeEntryHash(snapshot));
    }

    [Fact]
    public void RenderBinding_NeverContainsTheSecretItself()
    {
        // Only the name of the environment variable is recorded, and the package ships these bytes.
        var rendered = Encoding.UTF8.GetString(CanonicalPluginBindingForm.Render(LedgerTestData.Binding));

        Assert.Contains("secret_ref=PS_SECRET_ERP_TOKEN\n", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("Rationale", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_RefusesAnUnknownCanonicalForm()
    {
        var snapshot = LedgerTestData.Snapshot();
        snapshot.CanonicalFormVersion = "priorstate-snapshot-v99";

        Assert.Throws<InvalidOperationException>(() => CanonicalSnapshotForm.Render(snapshot));
    }

    [Fact]
    public void RenderVersion1_RefusesASnapshotWithoutConditions()
    {
        var snapshot = LedgerTestData.Snapshot();
        snapshot.Conditions = null;

        Assert.Throws<InvalidOperationException>(() => CanonicalSnapshotForm.Render(snapshot));
    }

    [Fact]
    public void RenderVersion2_RefusesASnapshotWithoutAnObservedPluginVersion()
    {
        var snapshot = LedgerTestData.PluginSnapshot();
        snapshot.PluginVersion = null;

        Assert.Throws<InvalidOperationException>(() => CanonicalSnapshotForm.Render(snapshot));
    }

    [Fact]
    public void PluginSnapshots_LinkIntoTheSameChainAsPageCaptures()
    {
        // The whole point of making these siblings rather than side data: one chain, one daily
        // Merkle root, one timestamp covering both.
        var page = LedgerTestData.Snapshot(sequence: 1);
        var plugin = LedgerTestData.PluginSnapshot(sequence: 2, previousHash: page.EntryHash);

        var result = HashChain.Verify([page, plugin]);

        Assert.True(result.IsIntact);
    }

    [Fact]
    public void BindingDigest_IsTheHashOfTheBindingForm()
    {
        var expected = Sha256Hash.FromBytes(
            System.Security.Cryptography.SHA256.HashData(
                CanonicalPluginBindingForm.Render(LedgerTestData.Binding)));

        Assert.Equal(expected, CanonicalPluginBindingForm.ComputeDigest(LedgerTestData.Binding));
    }
}
