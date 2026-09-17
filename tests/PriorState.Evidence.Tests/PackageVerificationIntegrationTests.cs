using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PriorState.Domain.Entities;
using PriorState.Domain.ValueObjects;
using PriorState.Ledger;
using PriorState.Storage;

namespace PriorState.Evidence.Tests;

// These tests deliberately fail if the verification tools are unavailable. CI installs them.
// Only storage and PDF rendering are stubbed; packages, canonical hashes, Merkle trees,
// timestamp certificates/tokens, and the exported shell script are real.
public sealed class PackageVerificationIntegrationTests : IClassFixture<TestTimestampAuthority>
{
    private readonly TestTimestampAuthority _tsa;

    public PackageVerificationIntegrationTests(TestTimestampAuthority tsa) => _tsa = tsa;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExportedPackageVerifiesWithIndependentRoot(bool plugin)
    {
        var package = await ExportAsync(plugin);
        var result = await VerifyAsync(package, "--ca-file", _tsa.RootCertificate);
        Assert.True(result.Code == 0, result.Output);
        Assert.Contains("RESULT: OK", result.Output);
    }

    [Theory]
    [InlineData("snapshot.wacz")]
    [InlineData("canonical/entry.txt")]
    [InlineData("merkle/audit-path.txt")]
    [InlineData("timestamp/token.tsr")]
    public async Task ModifiedPackageFails(string file)
    {
        var package = await ExportAsync(false);
        var path = Path.Combine(package, file);
        if (file == "merkle/audit-path.txt")
        {
            await File.WriteAllTextAsync(path, "R " + new string('0', 64) + "\n");
        }
        else
        {
            var bytes = await File.ReadAllBytesAsync(path);
            // CMS ends with the signer signature; its middle can contain an unused bundled CA.
            bytes[file == "timestamp/token.tsr" ? bytes.Length - 1 : bytes.Length / 2] ^= 1;
            await File.WriteAllBytesAsync(path, bytes);
        }
        var result = await VerifyAsync(package, "--ca-file", _tsa.RootCertificate);
        Assert.True(result.Code == 1, result.Output);
        Assert.Contains("RESULT: FAILED", result.Output);
    }

    [Theory]
    [InlineData("plugin/binding.txt")]
    [InlineData("plugin/configuration.json")]
    public async Task ModifiedPluginCommitmentsFail(string file)
    {
        var package = await ExportAsync(true);
        await File.AppendAllTextAsync(Path.Combine(package, file), "tampered");
        var result = await VerifyAsync(package, "--ca-file", _tsa.RootCertificate);
        Assert.True(result.Code == 1, result.Output);
    }

    [Fact]
    public async Task AttackerCanRebuildEverythingButCannotReplaceRecipientTrust()
    {
        // Attacker controls payload, metadata, Merkle root, token and bundled root certificate.
        var package = await ExportAsync(false, attacker: true);
        var rejected = await VerifyAsync(package, "--ca-file", _tsa.RootCertificate);
        Assert.True(rejected.Code == 1, rejected.Output);
        Assert.Contains("timestamp token did NOT verify", rejected.Output);
        // Demonstrate the fake package is internally consistent, isolating the trust boundary.
        var acceptedByAttackerRoot = await VerifyAsync(package, "--ca-file", _tsa.AttackerCertificate);
        Assert.True(acceptedByAttackerRoot.Code == 0, acceptedByAttackerRoot.Output);
    }

    [Fact]
    public async Task MissingCaArgumentFailsEvenWithBundledRoot()
    {
        var result = await VerifyAsync(await ExportAsync(false));
        Assert.True(result.Code == 2, result.Output);
        Assert.Contains("independently trusted CA file is required", result.Output);
    }

    [Fact]
    public async Task RelativeCaPathWithSpacesResolvesFromCallerDirectory()
    {
        var package = await ExportAsync(false);
        var result = await VerifyAsync(package, "--ca-file", "trusted authority/root.pem");
        Assert.True(result.Code == 0, result.Output);
    }

    [Fact]
    public async Task TokenContainingSignerVerifiesWithoutBundledChain()
    {
        var package = await ExportAsync(false);
        File.Delete(Path.Combine(package, "timestamp/tsa-chain.pem"));
        var result = await VerifyAsync(package, "--ca-file", _tsa.RootCertificate);
        Assert.True(result.Code == 0, result.Output);
    }

    private Task<TestTimestampAuthority.CommandResult> VerifyAsync(string package, params string[] arguments) =>
        TestTimestampAuthority.RunAsync(_tsa.DirectoryPath, new[] { "sh", Path.Combine(package, "verify.sh") }.Concat(arguments).ToArray());

    private async Task<string> ExportAsync(bool plugin, bool attacker = false)
    {
        var payload = Encoding.UTF8.GetBytes(attacker ? "attacker-controlled replacement" : "archived bytes\n");
        var conditions = new CaptureConditions
        {
            UserAgent = "test", ViewportWidth = 1280, ViewportHeight = 720,
            AuthenticatedSession = false, AdBlockerActive = false,
            CookieBanner = CookieBannerHandling.LeftAsIs, JavaScriptSettleMs = 100,
            ChromiumVersion = "test", CrawlerVersion = "test",
        };
        var snapshot = new Snapshot
        {
            Url = attacker ? "https://attacker.test" : "https://example.test/page",
            CapturedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
            PayloadSha256 = Sha256Hash.FromBytes(SHA256.HashData(payload)),
            PayloadObjectKey = "payload", PayloadSizeBytes = payload.Length,
            PayloadMediaType = plugin ? "application/json" : "application/wacz",
            CanonicalFormVersion = plugin ? CanonicalSnapshotForm.Version2 : CanonicalSnapshotForm.Version1,
            CaptureProfileVersion = new CaptureProfileVersion
            {
                Name = "test", Version = 1, Rationale = "integration test", Conditions = conditions,
            },
            Conditions = plugin ? null : conditions,
            ChainSequence = 1, PreviousHash = Sha256Hash.Genesis, EntryHash = Sha256Hash.Genesis,
            StorageWorm = WormSupport.Unsupported,
        };
        if (plugin)
        {
            snapshot.PluginVersion = "1.0.0";
            snapshot.PluginBindingVersion = new PluginBindingVersion
            {
                PluginId = "http-json", Name = "test", Version = 1,
                ConfigurationJson = "{\"url\":\"https://example.test/page\"}",
                Rationale = "integration test", Required = true, CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            };
        }
        snapshot.EntryHash = Sha256Hash.FromBytes(SHA256.HashData(CanonicalSnapshotForm.Render(snapshot)));
        Sha256Hash[] entries = [snapshot.EntryHash, Sha256Hash.FromBytes(SHA256.HashData("sibling"u8))];
        var root = MerkleTree.ComputeRoot(entries);
        var token = await _tsa.SignAsync(root.Value, attacker);
        var builder = new EvidencePackageBuilder(new PayloadStore(payload), new StubRenderer(),
            Options.Create(new EvidenceOptions { CaChainPemPath = attacker ? _tsa.AttackerCertificate : _tsa.RootCertificate }),
            NullLogger<EvidencePackageBuilder>.Instance);
        using var zip = new MemoryStream();
        await builder.BuildAsync(new EvidencePackageRequest
        {
            Snapshot = snapshot, LeafIndex = 0, AuditPath = MerkleTree.ComputeAuditPath(entries, 0),
            Anchor = new TimestampAnchor
            {
                CoversFromUtc = snapshot.CapturedAtUtc, CoversUntilUtc = snapshot.CapturedAtUtc,
                FirstChainSequence = 1, LastChainSequence = 2, MerkleRoot = root,
                TimestampToken = token, TsaUrl = "https://operator-assertion.invalid",
                TsaGeneralizedTime = DateTimeOffset.UtcNow, QualifiedProvider = false,
            },
        }, zip);
        var directory = Path.Combine(_tsa.DirectoryPath, "package " + Guid.NewGuid().ToString("N"));
        zip.Position = 0;
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read);
        archive.ExtractToDirectory(directory);
        return directory;
    }

    private sealed class StubRenderer : IProtocolRenderer
    {
        public Task<byte[]> RenderAsync(EvidencePackageRequest request, Guid packageId, CancellationToken cancellationToken = default) =>
            Task.FromResult("PDF renderer intentionally excluded from cryptographic verification tests"u8.ToArray());
    }

    private sealed class PayloadStore(byte[] payload) : IObjectStore
    {
        public WormSupport WormCapability => WormSupport.Unsupported;
        public Task<Stream> GetAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult<Stream>(new MemoryStream(payload));
        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<Stream> GetRangeAsync(string key, long firstByte, long lastByte, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ObjectWriteResult> PutAsync(string key, Stream content, string contentType, TimeSpan retention, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

public sealed class TestTimestampAuthority : IAsyncLifetime
{
    public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "priorstate verification " + Guid.NewGuid().ToString("N"));
    public string RootCertificate => Path.Combine(DirectoryPath, "trusted authority", "root.pem");
    public string AttackerCertificate => Path.Combine(DirectoryPath, "attacker", "root.pem");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(DirectoryPath);
        await RequireSuccessAsync(DirectoryPath, "sh", "-c", "command -v openssl && command -v xxd && command -v sha256sum");
        await CreateAuthorityAsync("trusted authority");
        await CreateAuthorityAsync("attacker");
    }

    private async Task CreateAuthorityAsync(string name)
    {
        var directory = Path.Combine(DirectoryPath, name);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "request.cnf"), "[req]\ndistinguished_name=dn\n[dn]\n");
        await File.WriteAllTextAsync(Path.Combine(directory, "signer.ext"), "basicConstraints=critical,CA:false\nkeyUsage=critical,digitalSignature\nextendedKeyUsage=critical,timeStamping\n");
        await RequireSuccessAsync(directory, "openssl", "req", "-new", "-x509", "-newkey", "rsa:2048", "-nodes", "-days", "2", "-subj", "/CN=" + name,
            "-config", "request.cnf", "-addext", "basicConstraints=critical,CA:true", "-addext", "keyUsage=critical,keyCertSign,cRLSign", "-keyout", "root.key", "-out", "root.pem");
        await RequireSuccessAsync(directory, "openssl", "req", "-new", "-newkey", "rsa:2048", "-nodes", "-subj", "/CN=Ephemeral test TSA", "-config", "request.cnf", "-keyout", "signer.key", "-out", "signer.csr");
        await RequireSuccessAsync(directory, "openssl", "x509", "-req", "-in", "signer.csr", "-CA", "root.pem", "-CAkey", "root.key", "-CAcreateserial", "-days", "2", "-extfile", "signer.ext", "-out", "signer.pem");
        await File.WriteAllTextAsync(Path.Combine(directory, "serial"), "01\n");
        await File.WriteAllTextAsync(Path.Combine(directory, "tsa.cnf"), """
            [tsa]
            default_tsa = test_tsa
            [test_tsa]
            serial = serial
            signer_cert = signer.pem
            certs = root.pem
            signer_key = signer.key
            signer_digest = sha256
            default_policy = 1.2.3.4.1
            digests = sha256
            accuracy = secs:1
            ordering = yes
            tsa_name = yes
            ess_cert_id_chain = no
            ess_cert_id_alg = sha256
            """);
    }

    public async Task<byte[]> SignAsync(string digest, bool attacker)
    {
        var directory = Path.Combine(DirectoryPath, attacker ? "attacker" : "trusted authority");
        await RequireSuccessAsync(directory, "openssl", "ts", "-query", "-digest", digest, "-sha256", "-cert", "-out", "query.tsq", "-config", "tsa.cnf");
        await RequireSuccessAsync(directory, "openssl", "ts", "-reply", "-config", "tsa.cnf", "-queryfile", "query.tsq", "-token_out", "-out", "token.der");
        return await File.ReadAllBytesAsync(Path.Combine(directory, "token.der"));
    }

    private static async Task RequireSuccessAsync(string cwd, params string[] arguments)
    {
        var result = await RunAsync(cwd, arguments);
        Assert.True(result.Code == 0, result.Output);
    }

    public static async Task<CommandResult> RunAsync(string cwd, params string[] arguments)
    {
        var gitBin = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "usr", "bin");
        var shell = OperatingSystem.IsWindows() ? Path.Combine(gitBin, "sh.exe") : "/bin/sh";
        var start = new ProcessStartInfo(shell) { WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        if (OperatingSystem.IsWindows())
        {
            start.Environment["PATH"] = gitBin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
            start.Environment["MSYS2_ARG_CONV_EXCL"] = "*";
        }
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("exec \"$@\"");
        start.ArgumentList.Add("test-command");
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(OperatingSystem.IsWindows() && Path.IsPathFullyQualified(argument)
                && !argument.StartsWith("/CN=", StringComparison.Ordinal)
                ? "/" + char.ToLowerInvariant(argument[0]) + argument[2..].Replace('\\', '/') : argument);
        }
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start verification shell; install sh, openssl, xxd and sha256sum.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        return new CommandResult(process.ExitCode, await stdout + await stderr);
    }

    public Task DisposeAsync()
    {
        // This exact per-fixture directory is created above; no shared or user paths are removed.
        if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, recursive: true);
        return Task.CompletedTask;
    }

    public sealed record CommandResult(int Code, string Output);
}
