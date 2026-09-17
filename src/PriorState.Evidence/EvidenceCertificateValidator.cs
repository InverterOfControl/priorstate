using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Options;

namespace PriorState.Evidence;

/// <summary>Checks configured certificate material, without treating it as independently trusted.</summary>
public sealed class EvidenceCertificateValidator : IValidateOptions<EvidenceOptions>
{
    public ValidateOptionsResult Validate(string? name, EvidenceOptions options)
    {
        try
        {
            _ = ReadConfiguredChain(options.CaChainPemPath);
            return ValidateOptionsResult.Success;
        }
        catch (InvalidOperationException exception)
        {
            return ValidateOptionsResult.Fail(exception.Message);
        }
    }

    /// <summary>Reads and validates the exact bytes that will be included in the package.</summary>
    public static byte[]? ReadConfiguredChain(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var bytes = File.ReadAllBytes(path);
            var pem = new UTF8Encoding(false, true).GetString(bytes);
            if (pem.Contains("PRIVATE KEY", StringComparison.Ordinal))
            {
                throw new CryptographicException("Only public certificates may be included.");
            }

            var remaining = pem.AsSpan();
            var count = 0;
            var hasCa = false;
            while (PemEncoding.TryFind(remaining, out var fields))
            {
                if (!remaining[fields.Label].SequenceEqual("CERTIFICATE"))
                {
                    throw new CryptographicException("Only CERTIFICATE PEM blocks are supported.");
                }

                using var certificate = X509Certificate2.CreateFromPem(remaining[fields.Location]);
                hasCa |= certificate.Extensions.OfType<X509BasicConstraintsExtension>()
                    .Any(extension => extension.CertificateAuthority);
                count++;
                remaining = remaining[fields.Location.End..];
            }

            // PEM import APIs skip malformed blocks. Count markers as well so a valid first
            // certificate cannot hide a broken second one.
            if (count == 0 || count != CountMarkers(pem, "-----BEGIN ")
                || count != CountMarkers(pem, "-----END ") || !hasCa)
            {
                throw new CryptographicException("Provide complete PEM certificates including a CA certificate.");
            }

            return bytes;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                         or CryptographicException or DecoderFallbackException)
        {
            throw new InvalidOperationException(
                "Evidence:CaChainPemPath must reference a readable PEM certificate bundle containing a CA "
                + "certificate. Check the mounted file and obtain certificates from your timestamp provider. "
                + "See deploy/tsa-certificates.md. " + exception.Message, exception);
        }
    }

    private static int CountMarkers(string value, string marker) =>
        value.Split(marker, StringSplitOptions.None).Length - 1;
}
