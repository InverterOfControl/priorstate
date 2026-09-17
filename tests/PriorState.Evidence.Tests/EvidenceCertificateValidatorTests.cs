using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PriorState.Evidence;

namespace PriorState.Evidence.Tests;

public sealed class EvidenceCertificateValidatorTests
{
    [Fact]
    public void OptionsValidationRejectsBrokenConfiguredBundle() =>
        WithFile("placeholder", path => Assert.True(new EvidenceCertificateValidator()
            .Validate(null, new EvidenceOptions { CaChainPemPath = path }).Failed));

    [Fact]
    public void UnconfiguredBundleIsOptional() =>
        Assert.Null(EvidenceCertificateValidator.ReadConfiguredChain(null));

    [Theory]
    [InlineData("")]
    [InlineData("# placeholder only")]
    [InlineData("-----BEGIN CERTIFICATE-----\nbroken\n-----END CERTIFICATE-----")]
    public void InvalidBundleFailsWithActionableMessage(string content)
    {
        WithFile(content, path =>
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                EvidenceCertificateValidator.ReadConfiguredChain(path));
            Assert.Contains("Evidence:CaChainPemPath", exception.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void MissingConfiguredFileIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() =>
            EvidenceCertificateValidator.ReadConfiguredChain(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pem")));
    }

    [Fact]
    public void ValidCaIsReturnedUnchanged()
    {
        var pem = CreateCertificate(true);
        WithFile(pem, path => Assert.Equal(File.ReadAllBytes(path),
            EvidenceCertificateValidator.ReadConfiguredChain(path)));
    }

    [Fact]
    public void LeafWithoutCaIsRejected() =>
        WithFile(CreateCertificate(false), path => Assert.Throws<InvalidOperationException>(() =>
            EvidenceCertificateValidator.ReadConfiguredChain(path)));

    [Theory]
    [InlineData("\n-----BEGIN CERTIFICATE-----\nbroken\n-----END CERTIFICATE-----")]
    [InlineData("\n-----BEGIN PRIVATE KEY-----\nbroken\n-----END PRIVATE KEY-----")]
    public void ValidCaCannotHideInvalidMaterial(string suffix) =>
        WithFile(CreateCertificate(true) + suffix, path => Assert.Throws<InvalidOperationException>(() =>
            EvidenceCertificateValidator.ReadConfiguredChain(path)));

    private static string CreateCertificate(bool isCa)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Test CA", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(isCa, false, 0, true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        return certificate.ExportCertificatePem();
    }

    private static void WithFile(string content, Action<string> assertion)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, content);
            assertion(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
