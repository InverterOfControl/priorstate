using System.Reflection;
using System.Text;
using PriorState.Evidence;

namespace PriorState.Evidence.Tests;

/// <summary>
/// Guards the two files that get shipped verbatim to the opposing party.
///
/// verify.sh is the trust anchor of the whole system: it is what a court-appointed expert reads
/// and runs. These tests are not about behaviour so much as about not silently breaking the
/// promises the README and the protocol make about it.
/// </summary>
public sealed class VerifyScriptTests
{
    private static string ReadResource(string name)
    {
        using var stream = typeof(EvidencePackageBuilder).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded resource '{name}' is missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string VerifyScript => ReadResource("PriorState.Evidence.Resources.verify.sh");

    private static string ProtocolTemplate => ReadResource("PriorState.Evidence.Resources.protocol.html");

    [Fact]
    public void VerifyScript_IsEmbeddedInTheAssembly()
    {
        Assert.False(string.IsNullOrWhiteSpace(VerifyScript));
    }

    [Fact]
    public void VerifyScript_UsesLfLineEndings()
    {
        // A script with CRLF endings fails on every POSIX shell with an unhelpful error. The
        // recipient would conclude the package is broken, which is the opposite of the point.
        Assert.DoesNotContain('\r', VerifyScript);
    }

    [Fact]
    public void VerifyScript_TargetsPlainShNotBash()
    {
        Assert.StartsWith("#!/bin/sh", VerifyScript, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("canonical/entry.txt")]
    [InlineData("manifest.txt")]
    [InlineData("snapshot.wacz")]
    [InlineData("timestamp/token.tsr")]
    [InlineData("timestamp/root.txt")]
    [InlineData("merkle/audit-path.txt")]
    public void VerifyScript_ReferencesEveryFileThePackageShips(string path)
    {
        Assert.Contains(path, VerifyScript, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyScript_PerformsAllFourChecks()
    {
        // Archive hash, entry hash, Merkle inclusion, timestamp signature. Losing any one of them
        // would leave the script reporting OK for a package it has not actually established.
        Assert.Contains("sha256_file snapshot.wacz", VerifyScript, StringComparison.Ordinal);
        Assert.Contains("sha256_stdin < canonical/entry.txt", VerifyScript, StringComparison.Ordinal);
        Assert.Contains("audit-path.txt", VerifyScript, StringComparison.Ordinal);
        Assert.Contains("openssl ts -verify", VerifyScript, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyScript_UsesTheSameMerkleDomainSeparationAsTheLedger()
    {
        // RFC 6962 prefixes: 0x00 for leaves, 0x01 for internal nodes. If these drift apart from
        // MerkleTree, every exported package stops verifying.
        Assert.Contains("printf '00%s'", VerifyScript, StringComparison.Ordinal);
        Assert.Contains("printf '01%s'", VerifyScript, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyScript_StatesWhatItDoesNotProve()
    {
        // Overclaiming is the failure mode that would actually damage someone relying on this.
        Assert.Contains("complete or representative", VerifyScript, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyScript_ExitsNonZeroWhenAnyCheckFails()
    {
        Assert.Contains("exit 1", VerifyScript, StringComparison.Ordinal);
        Assert.Contains("FAILURES=$((FAILURES + 1))", VerifyScript, StringComparison.Ordinal);
    }

    [Fact]
    public void ProtocolTemplate_HasAPlaceholderForEveryValueTheRendererSubstitutes()
    {
        // The renderer replaces {{Name}} tokens; a template missing one silently drops a fact
        // from a legal document. Checking the load-bearing ones by name.
        string[] required =
        [
            "{{Url}}", "{{CapturedAtUtc}}", "{{ProfileDesignation}}", "{{WaczSha256}}",
            "{{PreviousHash}}", "{{EntryHash}}", "{{MerkleRoot}}", "{{TsaUrl}}",
            "{{TsaGeneralizedTime}}", "{{TsaQualified}}", "{{TsaWarning}}", "{{StorageWorm}}",
            "{{ChainSequence}}", "{{ChromiumVersion}}", "{{CrawlerVersion}}",
        ];

        foreach (var placeholder in required)
        {
            Assert.Contains(placeholder, ProtocolTemplate, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ProtocolTemplate_StatesTheLimitsOfWhatItCertifies()
    {
        Assert.Contains("Grenzen dieses Protokolls", ProtocolTemplate, StringComparison.Ordinal);
        Assert.Contains("keine Rechtsberatung", ProtocolTemplate, StringComparison.Ordinal);
    }

    [Fact]
    public void EvidenceOptions_DefaultToTheCrawlerImageForRendering()
    {
        // Rendering with the Chromium that is already present is what keeps the dependency count
        // and the licence surface down; a different default would quietly undo that.
        var options = new EvidenceOptions();

        Assert.Contains("browsertrix-crawler", options.RendererImage, StringComparison.Ordinal);
    }
}
