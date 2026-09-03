using System.Security.Cryptography;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PriorState.Domain.Entities;
using PriorState.Storage;

namespace PriorState.Storage.Tests;

public sealed class S3ObjectStoreTests
{
    private static S3ObjectStore CreateStore(IAmazonS3 s3, WormSupport worm = WormSupport.Unsupported)
    {
        var options = Options.Create(new ObjectStoreOptions
        {
            AccessKey = "test",
            SecretKey = "test",
            ServiceUrl = "http://localhost:3900",
        });

        var state = new WormCapabilityState { Capability = worm };

        return new S3ObjectStore(s3, options, state, NullLogger<S3ObjectStore>.Instance);
    }

    [Fact]
    public async Task PutAsync_ReportsTheSizeEvenThoughTheSdkClosesTheStream()
    {
        // Regression: the AWS SDK disposes the request's InputStream once the upload finishes.
        // Reading buffer.Length after PutObjectAsync threw ObjectDisposedException, which failed
        // every real capture at the point of writing it to the ledger — after the crawl had
        // already succeeded. Unit tests with a permissive fake would not have caught it, so this
        // fake behaves like the SDK and closes the stream.
        var s3 = new StreamClosingS3Client();
        var store = CreateStore(s3);
        var payload = Encoding.UTF8.GetBytes("pretend this is a WACZ archive");

        using var content = new MemoryStream(payload);
        var result = await store.PutAsync("test/archive.wacz", content, "application/wacz", TimeSpan.FromDays(1));

        Assert.Equal(payload.Length, result.SizeBytes);
        Assert.True(s3.StreamWasClosed, "the fake did not reproduce the SDK behaviour being guarded against");
    }

    [Fact]
    public async Task PutAsync_HashesWhatWasActuallyStored()
    {
        var s3 = new StreamClosingS3Client();
        var store = CreateStore(s3);
        var payload = Encoding.UTF8.GetBytes("pretend this is a WACZ archive");
        var expected = Convert.ToHexStringLower(SHA256.HashData(payload));

        using var content = new MemoryStream(payload);
        var result = await store.PutAsync("test/archive.wacz", content, "application/wacz", TimeSpan.FromDays(1));

        Assert.Equal(expected, result.Sha256.Value);
        Assert.Equal(payload, s3.CapturedBytes);
    }

    [Fact]
    public async Task PutAsync_DoesNotApplyRetentionWhenTheBackendCannotEnforceIt()
    {
        var s3 = new StreamClosingS3Client();
        var store = CreateStore(s3, WormSupport.Unsupported);

        using var content = new MemoryStream([1, 2, 3]);
        var result = await store.PutAsync("test/a.wacz", content, "application/wacz", TimeSpan.FromDays(365));

        Assert.Null(s3.CapturedRequest?.ObjectLockMode);
        Assert.Equal(WormSupport.Unsupported, result.Worm);
        Assert.Null(result.RetainUntil);
    }

    [Fact]
    public async Task PutAsync_RecordsRetainUntilOnlyWhenEnforcementWasProven()
    {
        // A backend whose Object Lock API works but was never observed to refuse a delete gets
        // the retention applied — it costs nothing — but the snapshot must not carry a retention
        // date, because the evidence package would then assert protection nobody verified.
        var s3 = new StreamClosingS3Client();
        var store = CreateStore(s3, WormSupport.ApiPresentUnverified);

        using var content = new MemoryStream([1, 2, 3]);
        var result = await store.PutAsync("test/a.wacz", content, "application/wacz", TimeSpan.FromDays(365));

        Assert.Equal(ObjectLockMode.Compliance, s3.CapturedRequest?.ObjectLockMode);
        Assert.Equal(WormSupport.ApiPresentUnverified, result.Worm);
        Assert.Null(result.RetainUntil);
    }

    [Fact]
    public async Task PutAsync_RecordsRetainUntilWhenEnforcementWasProven()
    {
        var s3 = new StreamClosingS3Client();
        var store = CreateStore(s3, WormSupport.Enforced);

        using var content = new MemoryStream([1, 2, 3]);
        var result = await store.PutAsync("test/a.wacz", content, "application/wacz", TimeSpan.FromDays(365));

        Assert.Equal(WormSupport.Enforced, result.Worm);
        Assert.NotNull(result.RetainUntil);
    }

    /// <summary>
    /// Stands in for the SDK client and, crucially, closes the request stream the way the real one
    /// does. Subclassing AmazonS3Client rather than implementing IAmazonS3 keeps this to one
    /// overridden method instead of a hundred stubs.
    /// </summary>
    private sealed class StreamClosingS3Client : AmazonS3Client
    {
        public StreamClosingS3Client()
            : base(new BasicAWSCredentials("test", "test"), new AmazonS3Config
            {
                ServiceURL = "http://localhost:3900",
                ForcePathStyle = true,
                AuthenticationRegion = "test",
            })
        {
        }

        public PutObjectRequest? CapturedRequest { get; private set; }

        public byte[]? CapturedBytes { get; private set; }

        public bool StreamWasClosed { get; private set; }

        public override Task<PutObjectResponse> PutObjectAsync(
            PutObjectRequest request,
            CancellationToken cancellationToken = default)
        {
            CapturedRequest = request;

            using var copy = new MemoryStream();
            request.InputStream.CopyTo(copy);
            CapturedBytes = copy.ToArray();

            request.InputStream.Dispose();
            StreamWasClosed = true;

            return Task.FromResult(new PutObjectResponse());
        }
    }
}
