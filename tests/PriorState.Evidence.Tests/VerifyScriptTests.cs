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
    [InlineData("plugin/binding.txt")]
    [InlineData("plugin/configuration.json")]
    public void VerifyScript_ReferencesEveryFileThePackageShips(string path)
    {
        Assert.Contains(path, VerifyScript, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyScript_PerformsEveryCheck()
    {
        // Payload hash, entry hash, Merkle inclusion, timestamp signature, and — for a plugin
        // package — the configuration digest. Losing any one of them would leave the script
        // reporting OK for a package it has not actually established.
        Assert.Contains("sha256_file \"$PAYLOAD_FILE\"", VerifyScript, StringComparison.Ordinal);
        Assert.Contains("sha256_stdin < canonical/entry.txt", VerifyScript, StringComparison.Ordinal);
        Assert.Contains("audit-path.txt", VerifyScript, StringComparison.Ordinal);
        Assert.Contains("openssl ts -verify", VerifyScript, StringComparison.Ordinal);
        Assert.Contains("sha256_file plugin/binding.txt", VerifyScript, StringComparison.Ordinal);
        Assert.Contains("sha256_file plugin/configuration.json", VerifyScript, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyScript_RefusesACanonicalFormItDoesNotKnow()
    {
        // A package written by a newer PriorState must stop rather than fall through to a default
        // and report OK against fields it never read.
        Assert.Contains("Unknown canonical form", VerifyScript, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyScript_HandlesBothCanonicalForms()
    {
        Assert.Contains("priorstate-snapshot-v1)", VerifyScript, StringComparison.Ordinal);
        Assert.Contains("priorstate-snapshot-v2)", VerifyScript, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyScript_PassesTokenInBecauseTheStoredTokenIsBare()
    {
        // The package stores a bare TimeStampToken, not a TimeStampResp. Without -token_in openssl
        // fails with an ASN.1 tag error, and the script reports a valid timestamp as invalid — the
        // worst possible direction for this particular check to fail in.
        //
        // Asserting on the whole `ts -verify` invocation rather than on the flag alone: -token_in
        // already appears further down in the line that prints the asserted time, so a bare
        // Contains check passes even when the verification itself has lost the flag.
        var verifyInvocation = VerifyScript[VerifyScript.IndexOf("openssl ts -verify", StringComparison.Ordinal)..];
        verifyInvocation = verifyInvocation[..verifyInvocation.IndexOf("; then", StringComparison.Ordinal)];

        Assert.Contains("-token_in", verifyInvocation, StringComparison.Ordinal);
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
            "{{Url}}", "{{CapturedAtUtc}}", "{{ProfileDesignation}}", "{{PayloadHashRow}}",
            "{{PreviousHash}}", "{{EntryHash}}", "{{MerkleRoot}}", "{{TsaUrl}}",
            "{{TsaGeneralizedTime}}", "{{TsaQualified}}", "{{TsaWarning}}", "{{StorageWorm}}",
            "{{ChainSequence}}", "{{CaptureContextBlock}}", "{{PayloadSummaryRow}}",
            "{{ProtocolSubtitle}}", "{{ScopeNotice}}",
        ];

        foreach (var placeholder in required)
        {
            Assert.Contains(placeholder, ProtocolTemplate, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ProtocolTemplate_StatesTheLimitsOfWhatItCertifies()
    {
        Assert.Contains("{{ScopeNotice}}", ProtocolTemplate, StringComparison.Ordinal);
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
