using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PriorState.Domain.ValueObjects;

namespace PriorState.Ledger.Timestamping;

/// <summary>
/// Speaks the RFC-3161 HTTP protocol to a timestamp authority.
///
/// The request and response handling come from the base class library
/// (<see cref="Rfc3161TimestampRequest"/> in System.Security.Cryptography.Pkcs), so the part of
/// this system that has to hold up under adversarial scrutiny does not depend on a third-party
/// crypto implementation.
/// </summary>
public sealed partial class Rfc3161TimestampAuthority : ITimestampAuthority
{
    private const string RequestContentType = "application/timestamp-query";
    private const string ResponseContentType = "application/timestamp-reply";

    private readonly HttpClient _httpClient;
    private readonly TimestampAuthorityOptions _options;
    private readonly ILogger<Rfc3161TimestampAuthority> _logger;

    public Rfc3161TimestampAuthority(
        HttpClient httpClient,
        IOptions<TimestampAuthorityOptions> options,
        ILogger<Rfc3161TimestampAuthority> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TimestampResult> TimestampAsync(Sha256Hash hash, CancellationToken cancellationToken = default)
    {
        // A nonce ties this specific response to this specific request, so a recorded reply from
        // an earlier exchange cannot be substituted.
        var nonce = RandomNumberGenerator.GetBytes(16);

        var request = Rfc3161TimestampRequest.CreateFromHash(
            hash.ToBytes(),
            HashAlgorithmName.SHA256,
            requestedPolicyId: _options.RequestedPolicyOid is { Length: > 0 } oid ? new Oid(oid) : null,
            nonce: nonce,
            requestSignerCertificates: _options.RequestSignerCertificate);

        using var content = new ByteArrayContent(request.Encode());
        content.Headers.ContentType = new MediaTypeHeaderValue(RequestContentType);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsync(new Uri(_options.Url), content, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new TimestampAuthorityException(
                $"Could not reach the timestamp authority at {_options.Url}.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new TimestampAuthorityException(
                    $"Timestamp authority {_options.Url} returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not null && !string.Equals(mediaType, ResponseContentType, StringComparison.OrdinalIgnoreCase))
            {
                LogUnexpectedContentType(_options.Url, mediaType, ResponseContentType);
            }

            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);

            Rfc3161TimestampToken token;
            try
            {
                token = request.ProcessResponse(body, out _);
            }
            catch (CryptographicException ex)
            {
                throw new TimestampAuthorityException(
                    $"Timestamp authority {_options.Url} returned a response that could not be validated "
                    + "against the request. The token was not accepted.", ex);
            }

            // ProcessResponse already checks the nonce and the message imprint against the
            // request, so reaching this point means the token commits to our hash. Re-assert it
            // anyway: this is the one invariant the whole archive rests on.
            if (!token.TokenInfo.GetMessageHash().Span.SequenceEqual(hash.ToBytes()))
            {
                throw new TimestampAuthorityException(
                    "The timestamp token does not commit to the hash that was submitted.");
            }

            LogTimestamped(hash.Value, token.TokenInfo.Timestamp, _options.DisplayName, _options.Qualified);

            if (!_options.Qualified)
            {
                LogUnqualifiedAuthority(_options.DisplayName);
            }

            return new TimestampResult
            {
                Token = token.AsSignedCms().Encode(),
                GeneralizedTime = token.TokenInfo.Timestamp,
                TsaUrl = _options.Url,
                Qualified = _options.Qualified,
            };
        }
    }

    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "Timestamped {Hash} at {AssertedTime} by {Authority} (qualified: {Qualified}).")]
    private partial void LogTimestamped(string hash, DateTimeOffset assertedTime, string authority, bool qualified);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = "Timestamp authority {Authority} is not marked as a qualified eIDAS provider. Anchors "
                  + "created against it are suitable for evaluation, not for a dispute.")]
    private partial void LogUnqualifiedAuthority(string authority);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Warning,
        Message = "Timestamp authority {Url} replied with content type {ContentType}, expected {Expected}.")]
    private partial void LogUnexpectedContentType(string url, string contentType, string expected);
}
