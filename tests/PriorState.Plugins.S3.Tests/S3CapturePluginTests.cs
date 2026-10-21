using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using PriorState.Domain.Entities;
using PriorState.Plugins.Abstractions;

namespace PriorState.Plugins.S3.Tests;

public sealed class S3CapturePluginTests
{
    private const string Configuration = """{"endpoint":"https://objects.test","bucket":"documents","key":"prices/data.bin","versionId":"fixed"}""";

    [Fact]
    public async Task PreservesBytesAndUsesExplicitSessionCredentials()
    {
        var bytes = new byte[] { 0, 255, 13, 10 };
        var client = new StubClient(new MemoryStream(bytes), bytes.Length);
        ImmutableCredentials? seen = null;
        var plugin = new S3CapturePlugin(Microsoft.Extensions.Options.Options.Create(Options()), (credentials, config) =>
        {
            seen = credentials.GetCredentials();
            Assert.Equal("https://objects.test/", config.ServiceURL);
            return client;
        });
        var payload = await plugin.ExecuteAsync(Context());
        Assert.Equal(bytes, payload.Content);
        Assert.Equal("access", seen!.AccessKey);
        Assert.Equal("private", seen.SecretKey);
        Assert.Equal("session", seen.Token);
        Assert.Equal("fixed", client.Request!.VersionId);
        Assert.Equal("prices/data.bin", client.Request.Key);
        Assert.Equal("s3://documents/prices/data.bin?versionId=fixed", payload.Url);
        Assert.Equal("application/octet-stream", payload.MediaType);
        Assert.NotNull(payload.CapturedAtUtc);
    }

    [Fact]
    public async Task BoundsStreamEvenWhenReportedLengthIsTooSmall()
    {
        var options = Options();
        options.MaxPayloadBytes = 4;
        var plugin = new S3CapturePlugin(Microsoft.Extensions.Options.Options.Create(options),
            (_, _) => new StubClient(new MemoryStream(new byte[5]), 0));
        var error = await Assert.ThrowsAsync<PluginException>(() => plugin.ExecuteAsync(Context()));
        Assert.Contains("limit", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesTruncatedResponse()
    {
        var plugin = new S3CapturePlugin(Microsoft.Extensions.Options.Options.Create(Options()),
            (_, _) => new StubClient(new MemoryStream(new byte[3]), 5));
        var error = await Assert.ThrowsAsync<PluginException>(() => plugin.ExecuteAsync(Context()));
        Assert.Contains("incomplete", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeadlineCoversBodyRead()
    {
        var options = Options();
        options.Timeout = TimeSpan.FromMilliseconds(50);
        var plugin = new S3CapturePlugin(Microsoft.Extensions.Options.Options.Create(options), (_, _) => new StubClient(new StalledStream(), 0));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => plugin.ExecuteAsync(Context()).WaitAsync(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task MissingSecretNeverFallsBackToHostCredentials()
    {
        var plugin = new S3CapturePlugin(Microsoft.Extensions.Options.Options.Create(Options()), (_, _) => throw new InvalidOperationException("Must not create client"));
        await Assert.ThrowsAsync<PluginException>(() => plugin.ExecuteAsync(Context() with { Secret = null }));
    }

    [Fact]
    public async Task HostPolicyDefaultsToDeny()
    {
        var plugin = new S3CapturePlugin(Microsoft.Extensions.Options.Options.Create(new S3CaptureOptions()));
        await Assert.ThrowsAsync<PluginException>(() => plugin.ExecuteAsync(Context()));
    }

    [Fact]
    public async Task BlankComposeAllowlistDoesNotPermitAnyHost()
    {
        var options = new S3CaptureOptions();
        options.AllowedHosts.Add("");
        options.AllowedHosts.Add("  ");
        var plugin = new S3CapturePlugin(Microsoft.Extensions.Options.Options.Create(options));
        await Assert.ThrowsAsync<PluginException>(() => plugin.ExecuteAsync(Context()));
    }

    [Theory]
    [InlineData("https://attacker.test/file")]
    [InlineData("http://objects.test/file")]
    [InlineData("https://objects.test:8443/file")]
    [InlineData("https://objects.test.attacker.test/file")]
    public async Task SdkRedirectCannotSendSignedRequestToAnotherOrigin(string target)
    {
        var sent = false;
        using var http = new HttpClient(new S3EndpointGuard(new Uri("https://objects.test"), "documents", true,
            new Responder(_ => { sent = true; return new HttpResponseMessage(System.Net.HttpStatusCode.OK); })));
        using var request = new HttpRequestMessage(HttpMethod.Get, target);
        request.Headers.TryAddWithoutValidation("Authorization", "AWS4-HMAC-SHA256 private-signature");
        await Assert.ThrowsAsync<PluginException>(() => http.SendAsync(request));
        Assert.False(sent);
    }

    [Theory]
    [InlineData(true, "https://objects.test/documents/key")]
    [InlineData(false, "https://documents.objects.test/key")]
    public async Task EndpointGuardAllowsOnlyExpectedAddressingHost(bool pathStyle, string target)
    {
        using var http = new HttpClient(new S3EndpointGuard(new Uri("https://objects.test"), "documents", pathStyle,
            new Responder(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK))));
        using var response = await http.GetAsync(target);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("https://user:password@objects.test")]
    [InlineData("https://objects.test?token=secret")]
    [InlineData("https://objects.test/path")]
    public void RejectsUnsafeEndpoint(string endpoint)
    {
        var plugin = new S3CapturePlugin(Microsoft.Extensions.Options.Options.Create(Options()));
        var json = System.Text.Json.JsonSerializer.Serialize(new { endpoint, bucket = "documents", key = "file" });
        Assert.Throws<PluginException>(() => plugin.ValidateConfiguration(json, "test", "PS_SECRET_S3"));
    }

    private static S3CaptureOptions Options()
    {
        var options = new S3CaptureOptions();
        options.AllowedHosts.Add("objects.test");
        return options;
    }

    private static PluginExecutionContext Context() => new()
    {
        RunId = Guid.NewGuid(), ProjectId = Guid.NewGuid(), Profile = null!,
        Binding = new PluginBindingVersion { PluginId = "s3", Name = "documents", Version = 1, Rationale = "Test", Required = false, ConfigurationJson = Configuration },
        Secret = """{"accessKeyId":"access","secretAccessKey":"private","sessionToken":"session"}""",
    };

    private sealed class StubClient(Stream stream, long length) : AmazonS3Client(new AnonymousAWSCredentials(), new AmazonS3Config { ServiceURL = "https://objects.test" })
    {
        public GetObjectRequest? Request { get; private set; }
        public override Task<GetObjectResponse> GetObjectAsync(GetObjectRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(new GetObjectResponse { ResponseStream = stream, ContentLength = length });
        }
    }

    private sealed class StalledStream : MemoryStream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }

    private sealed class Responder(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
