using System.Globalization;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using PriorState.Domain.Entities;

namespace PriorState.Storage;

/// <summary>
/// Establishes what the configured object store actually does about immutability.
///
/// This exists because the honest answer changed. The original design assumed S3 Object Lock
/// would carry the WORM guarantee, but as of 2026 no self-hostable S3 implementation delivers it
/// dependably: MinIO's community edition is unmaintained, Garage has not merged Object Lock at
/// all, and SeaweedFS exposes the API while there are open reports that COMPLIANCE mode does not
/// actually refuse deletes.
///
/// An advertised capability is therefore not evidence of anything. The probe tests behaviour:
///
///   1. Ask the bucket for its object-lock configuration. No configuration means Unsupported.
///   2. Write a small scratch object with a COMPLIANCE-mode retention a minute into the future.
///   3. Try to delete it. If the backend refuses, retention is Enforced. If the delete succeeds,
///      the API is present but does nothing, which is recorded as ApiPresentUnverified.
///
/// The outcome is stored on every snapshot and printed in every evidence package. A snapshot
/// whose storage never enforced WORM is still fully provable — the hash chain and the RFC-3161
/// token are what carry the guarantee — but the protocol says so plainly rather than implying a
/// protection that was never there.
/// </summary>
public sealed partial class WormCapabilityProbe
{
    private const string ProbeKeyPrefix = ".priorstate-worm-probe/";
    private static readonly TimeSpan ProbeRetention = TimeSpan.FromMinutes(1);

    private readonly IAmazonS3 _s3;
    private readonly ILogger<WormCapabilityProbe> _logger;

    public WormCapabilityProbe(IAmazonS3 s3, ILogger<WormCapabilityProbe> logger)
    {
        _s3 = s3;
        _logger = logger;
    }

    public async Task<WormSupport> ProbeAsync(
        ObjectStoreOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.UseObjectLock)
        {
            LogObjectLockDisabled();
            return WormSupport.Unsupported;
        }

        if (!await BucketAdvertisesObjectLockAsync(options.Bucket, cancellationToken))
        {
            LogNoObjectLockConfiguration(options.Bucket, options.ServiceUrl);
            return WormSupport.Unsupported;
        }

        if (!options.ProbeWormEnforcement)
        {
            LogEnforcementProbeSkipped();
            return WormSupport.ApiPresentUnverified;
        }

        return await ProbeEnforcementAsync(options.Bucket, cancellationToken);
    }

    private async Task<bool> BucketAdvertisesObjectLockAsync(string bucket, CancellationToken cancellationToken)
    {
        try
        {
            var configuration = await _s3.GetObjectLockConfigurationAsync(
                new GetObjectLockConfigurationRequest { BucketName = bucket },
                cancellationToken);

            return configuration.ObjectLockConfiguration?.ObjectLockEnabled == ObjectLockEnabled.Enabled;
        }
        catch (AmazonS3Exception)
        {
            // Garage returns an error for the whole endpoint; AWS returns
            // ObjectLockConfigurationNotFoundError when the bucket was created without it. Either
            // way there is nothing to rely on.
            return false;
        }
    }

    private async Task<WormSupport> ProbeEnforcementAsync(string bucket, CancellationToken cancellationToken)
    {
        var key = ProbeKeyPrefix + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture);
        var retainUntil = DateTime.UtcNow.Add(ProbeRetention);

        try
        {
            using var body = new MemoryStream(Encoding.UTF8.GetBytes(
                "PriorState WORM enforcement probe. Safe to ignore; expires within a minute."));

            await _s3.PutObjectAsync(
                new PutObjectRequest
                {
                    BucketName = bucket,
                    Key = key,
                    InputStream = body,
                    ContentType = "text/plain",
                    ObjectLockMode = ObjectLockMode.Compliance,
                    ObjectLockRetainUntilDate = retainUntil,
                },
                cancellationToken);
        }
        catch (AmazonS3Exception ex)
        {
            LogProbeWriteRejected(ex.Message);
            return WormSupport.Unsupported;
        }

        try
        {
            await _s3.DeleteObjectAsync(
                new DeleteObjectRequest { BucketName = bucket, Key = key },
                cancellationToken);
        }
        catch (AmazonS3Exception)
        {
            // The delete was refused, which is exactly what retention is supposed to do.
            LogWormEnforced();
            return WormSupport.Enforced;
        }

        // The delete succeeded despite a COMPLIANCE-mode retention in the future. The backend
        // accepted the retention setting and then ignored it.
        LogWormNotEnforced();
        return WormSupport.ApiPresentUnverified;
    }

    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Information,
        Message = "Object Lock is disabled in configuration. Snapshot integrity rests on the hash chain "
                  + "and RFC-3161 timestamps alone, which is sufficient but will be stated as such in "
                  + "every evidence package.")]
    private partial void LogObjectLockDisabled();

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Warning,
        Message = "Bucket {Bucket} at {Endpoint} has no Object Lock configuration. Storage-level WORM is "
                  + "unavailable; evidence packages will record StorageWorm=Unsupported. See the storage "
                  + "page in the documentation for backends that do enforce it.")]
    private partial void LogNoObjectLockConfiguration(string bucket, string endpoint);

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Warning,
        Message = "Object Lock is configured but the enforcement probe is switched off, so it cannot be "
                  + "confirmed. Recording ApiPresentUnverified.")]
    private partial void LogEnforcementProbeSkipped();

    [LoggerMessage(
        EventId = 2003,
        Level = LogLevel.Warning,
        Message = "The backend rejected a write carrying Object Lock retention: {Reason}. "
                  + "Recording StorageWorm=Unsupported.")]
    private partial void LogProbeWriteRejected(string reason);

    [LoggerMessage(
        EventId = 2004,
        Level = LogLevel.Information,
        Message = "Object Lock verified: the backend refused to delete an object under retention.")]
    private partial void LogWormEnforced();

    [LoggerMessage(
        EventId = 2005,
        Level = LogLevel.Warning,
        Message = "The backend accepted an Object Lock retention and then allowed the object to be "
                  + "deleted anyway. Its WORM support is nominal, not real. Recording "
                  + "ApiPresentUnverified so no evidence package claims otherwise.")]
    private partial void LogWormNotEnforced();
}
