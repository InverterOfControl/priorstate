using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PriorState.Storage;

public static class StorageServiceCollectionExtensions
{
    public static IServiceCollection AddPriorStateStorage(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ObjectStoreOptions>()
            .Bind(configuration.GetSection(ObjectStoreOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IAmazonS3>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<ObjectStoreOptions>>().Value;

            return new AmazonS3Client(
                new BasicAWSCredentials(options.AccessKey, options.SecretKey),
                new AmazonS3Config
                {
                    ServiceURL = options.ServiceUrl,
                    ForcePathStyle = options.ForcePathStyle,
                    AuthenticationRegion = options.Region,
                });
        });

        services.AddSingleton<WormCapabilityProbe>();
        services.AddSingleton<WormCapabilityState>();
        services.AddSingleton<IObjectStore, S3ObjectStore>();
        services.AddHostedService<StorageInitializer>();

        return services;
    }
}

/// <summary>
/// Creates the bucket if it is missing and resolves the WORM capability once, before anything
/// else touches storage. Failing here is intentional: a snapshot written before the capability is
/// known could not be labelled honestly.
/// </summary>
internal sealed partial class StorageInitializer : IHostedService
{
    private readonly IAmazonS3 _s3;
    private readonly ObjectStoreOptions _options;
    private readonly WormCapabilityProbe _probe;
    private readonly WormCapabilityState _state;
    private readonly ILogger<StorageInitializer> _logger;

    public StorageInitializer(
        IAmazonS3 s3,
        IOptions<ObjectStoreOptions> options,
        WormCapabilityProbe probe,
        WormCapabilityState state,
        ILogger<StorageInitializer> logger)
    {
        _s3 = s3;
        _options = options.Value;
        _probe = probe;
        _state = state;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await EnsureBucketAsync(cancellationToken);

        _state.Capability = await _probe.ProbeAsync(_options, cancellationToken);
        LogCapabilityResolved(_options.ServiceUrl, _options.Bucket, _state.Capability);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task EnsureBucketAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _s3.GetBucketLocationAsync(_options.Bucket, cancellationToken);
            return;
        }
        catch (AmazonS3Exception)
        {
            // Falls through to creation below.
        }

        try
        {
            await _s3.PutBucketAsync(
                new Amazon.S3.Model.PutBucketRequest
                {
                    BucketName = _options.Bucket,
                    // Requesting Object Lock at creation is the only moment it can be enabled on
                    // most implementations. Backends that do not know the header ignore it.
                    ObjectLockEnabledForBucket = _options.UseObjectLock,
                },
                cancellationToken);

            LogBucketCreated(_options.Bucket);
        }
        catch (AmazonS3Exception ex)
        {
            throw new ObjectStoreException(
                $"Bucket '{_options.Bucket}' does not exist and could not be created at {_options.ServiceUrl}.", ex);
        }
    }

    [LoggerMessage(
        EventId = 2200,
        Level = LogLevel.Information,
        Message = "Created bucket {Bucket}.")]
    private partial void LogBucketCreated(string bucket);

    [LoggerMessage(
        EventId = 2201,
        Level = LogLevel.Information,
        Message = "Object storage ready at {Endpoint}, bucket {Bucket}. Storage WORM capability: {Capability}.")]
    private partial void LogCapabilityResolved(string endpoint, string bucket, Domain.Entities.WormSupport capability);
}
