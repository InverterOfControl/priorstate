using System.Text;
using PriorState.Domain.ValueObjects;
using PriorState.Ledger;

namespace PriorState.Ledger.Tests;

/// <summary>
/// These tests pin the canonical form. If one of them fails because the format changed, that is
/// not a test to update — it means every evidence package ever exported has become unverifiable.
/// Add a new format version instead.
/// </summary>
public sealed class CanonicalSnapshotFormTests
{
    [Fact]
    public void Render_ProducesTheExactExpectedBytes()
    {
        var snapshot = LedgerTestData.Snapshot();

        var rendered = Encoding.UTF8.GetString(CanonicalSnapshotForm.Render(snapshot));

        const string expected =
            "priorstate-snapshot-v1\n"
            + "sequence=1\n"
            + "prev=0000000000000000000000000000000000000000000000000000000000000000\n"
            + "url=https://example.com/prices\n"
            + "final_url=\n"
            + "captured_at=2026-09-03T14:30:00Z\n"
            + "wacz_sha256=e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855\n"
            + "wacz_size=1048576\n"
            + "profile=DE-Standard v1\n"
            + "user_agent=Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 Chrome/140.0.0.0 Safari/537.36\n"
            + "viewport=1920x1080\n"
            + "authenticated=false\n"
            + "adblock=false\n"
            + "cookie_banner=left_as_is\n"
            + "js_settle_ms=5000\n"
            + "chromium=140.0.7259.68\n"
            + "crawler=1.7.1\n";

        Assert.Equal(expected, rendered);
    }

    [Fact]
    public void Render_EmitsNoByteOrderMark()
    {
        var bytes = CanonicalSnapshotForm.Render(LedgerTestData.Snapshot());

        Assert.NotEqual(0xEF, bytes[0]);
    }

    [Fact]
    public void Render_UsesLfOnlyNeverCrLf()
    {
        var bytes = CanonicalSnapshotForm.Render(LedgerTestData.Snapshot());

        Assert.DoesNotContain((byte)'\r', bytes);
    }

    [Fact]
    public void Render_NormalisesNonUtcTimestampsToUtc()
    {
        var berlinSummer = LedgerTestData.Snapshot();
        berlinSummer.CapturedAtUtc = new DateTimeOffset(2026, 9, 3, 16, 30, 0, TimeSpan.FromHours(2));

        var rendered = Encoding.UTF8.GetString(CanonicalSnapshotForm.Render(berlinSummer));

        Assert.Contains("captured_at=2026-09-03T14:30:00Z\n", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_TruncatesSubSecondPrecision()
    {
        var snapshot = LedgerTestData.Snapshot();
        snapshot.CapturedAtUtc = new DateTimeOffset(2026, 9, 3, 14, 30, 0, 750, TimeSpan.Zero);

        var rendered = Encoding.UTF8.GetString(CanonicalSnapshotForm.Render(snapshot));

        Assert.Contains("captured_at=2026-09-03T14:30:00Z\n", rendered, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://example.com/a\nurl=https://evil.example/b", "https://example.com/a\\nurl=https://evil.example/b")]
    [InlineData("back\\slash", "back\\\\slash")]
    [InlineData("carriage\rreturn", "carriage\\rreturn")]
    public void Render_EscapesCharactersThatCouldForgeAField(string url, string expectedEscaped)
    {
        var snapshot = LedgerTestData.Snapshot(url: url);

        var rendered = Encoding.UTF8.GetString(CanonicalSnapshotForm.Render(snapshot));

        Assert.Contains($"url={expectedEscaped}\n", rendered, StringComparison.Ordinal);

        // The whole point: an injected newline must not create an extra line.
        Assert.Equal(17, rendered.Split('\n').Length - 1);
    }

    [Fact]
    public void ComputeEntryHash_IsStableAcrossCalls()
    {
        var snapshot = LedgerTestData.Snapshot();

        Assert.Equal(CanonicalSnapshotForm.ComputeEntryHash(snapshot), CanonicalSnapshotForm.ComputeEntryHash(snapshot));
    }

    [Fact]
    public void ComputeEntryHash_ChangesWhenAnyRecordedFieldChanges()
    {
        var original = LedgerTestData.Snapshot();
        var baseline = CanonicalSnapshotForm.ComputeEntryHash(original);

        var altered = LedgerTestData.Snapshot(url: "https://example.com/prices?changed");

        Assert.NotEqual(baseline, CanonicalSnapshotForm.ComputeEntryHash(altered));
    }

    [Fact]
    public void ComputeEntryHash_IgnoresExtractedTextWhichIsNotPartOfTheCommitment()
    {
        var snapshot = LedgerTestData.Snapshot();
        var before = CanonicalSnapshotForm.ComputeEntryHash(snapshot);

        snapshot.ExtractedText = "Search index content, derived and reproducible from the WACZ.";

        Assert.Equal(before, CanonicalSnapshotForm.ComputeEntryHash(snapshot));
    }

    [Fact]
    public void Render_RequiresTheCaptureProfileToBeLoaded()
    {
        var snapshot = LedgerTestData.Snapshot();
        snapshot.CaptureProfileVersion = null;

        var ex = Assert.Throws<InvalidOperationException>(() => CanonicalSnapshotForm.Render(snapshot));
        Assert.Contains("CaptureProfileVersion", ex.Message, StringComparison.Ordinal);
    }
}
