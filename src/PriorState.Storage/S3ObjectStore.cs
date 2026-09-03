using System.Security.Cryptography;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PriorState.Domain.Entities;
using PriorState.Domain.ValueObjects;

namespace PriorState.Storage;

/// <summary>
/// S3-compatible object store. Works against Garage (the bundled default), AWS S3, Backblaze B2,
/// Wasabi, Ceph RGW and anything else speaking the same API.
///
/// Nothing here depends on a vendor extension. That is a deliberate constraint: the storage
/// backend for an archive with a six-to-ten-year retention has to be replaceable, and the recent
/// history of self-hosted S3 implementations is reason enough not to marry one.
/// </summary>
public sealed partial class S3ObjectStore : IObjectStore
{
    private readonly IAmazonS3 _s3;
    private readonly ObjectStoreOptions _options;
    private readonly ILogger<S3ObjectStore> _logger;

    public S3ObjectStore(
        IAmazonS3 s3,
        IOptions<ObjectStoreOptions> options,
        WormCapabilityState wormState,
        ILogger<S3ObjectStore> logger)
    {
        ArgumentNullException.ThrowIfNull(wormState);

        _s3 = s3;
        _options = options.Value;
        _logger = logger;
        WormCapability = wormState.Capability;
    }

    public WormSupport WormCapability { get; }

    public async Task<ObjectWriteResult> PutAsync(
        string key,
        Stream content,
        string contentType,
        TimeSpan retention,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(content);

        // Hash while buffering, so the recorded digest is provably the digest of what was stored
        // rather than of something computed earlier from a different stream.
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;
        var sha256 = Sha256Hash.FromBytes(await SHA256.HashDataAsync(buffer, cancellationToken));
        buffer.Position = 0;

        var applyLock = WormCapability is WormSupport.Enforced or WormSupport.ApiPresentUnverified;
        DateTimeOffset? retainUntil = applyLock ? DateTimeOffset.UtcNow.Add(retention) : null;

        var request = new PutObjectRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            InputStream = buffer,
            ContentType = contentType,
            ChecksumSHA256 = Convert.ToBase64String(sha256.ToBytes()),
        };

        if (applyLock)
        {
            request.ObjectLockMode = ObjectLockMode.Compliance;
            request.ObjectLockRetainUntilDate = retainUntil!.Value.UtcDateTime;
        }

        try
        {
            await _s3.PutObjectAsync(request, cancellationToken);
        }
        catch (AmazonS3Exception ex)
        {
            throw new ObjectStoreException($"Could not store object '{key}' in bucket '{_options.Bucket}'.", ex);
        }

        LogStored(key, buffer.Length, WormCapability);

        return new ObjectWriteResult
        {
            Key = key,
            SizeBytes = buffer.Length,
            Sha256 = sha256,
            // Only a probe that actually saw a delete refused earns the Enforced label on a
            // snapshot. Anything weaker is reported as what it is.
            Worm = WormCapability,
            RetainUntil = WormCapability == WormSupport.Enforced ? retainUntil : null,
        };
    }

    public async Task<Stream> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        try
        {
            var response = await _s3.GetObjectAsync(_options.Bucket, key, cancellationToken);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex)
        {
            throw new ObjectStoreException($"Could not read object '{key}' from bucket '{_options.Bucket}'.", ex);
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        try
        {
            await _s3.GetObjectMetadataAsync(_options.Bucket, key, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    [LoggerMessage(
        EventId = 2100,
        Level = LogLevel.Debug,
        Message = "Stored {Key} ({SizeBytes} bytes), storage WORM: {Worm}.")]
    private partial void LogStored(string key, long sizeBytes, WormSupport worm);
}

/// <summary>
/// The probe result, resolved once at startup and injected wherever the answer is needed. A
/// singleton rather than a per-call probe: the answer cannot change while the process runs, and
/// probing on every write would leave scratch objects behind.
/// </summary>
public sealed class WormCapabilityState
{
    public WormSupport Capability { get; internal set; } = WormSupport.Unsupported;
}
