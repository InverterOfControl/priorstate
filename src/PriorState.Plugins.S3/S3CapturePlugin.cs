using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using PriorState.Plugins.Abstractions;

namespace PriorState.Plugins.S3;

/// <summary>Reads one source object without sharing the host's evidence-store credentials.</summary>
public sealed class S3CapturePlugin : ICapturePlugin
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private readonly S3CaptureOptions _options;
    private readonly Func<AWSCredentials, AmazonS3Config, IAmazonS3> _clientFactory;

    public S3CapturePlugin(IOptions<S3CaptureOptions> options)
        : this(options, static (credentials, config) => new AmazonS3Client(credentials, config)) { }

    public S3CapturePlugin(IOptions<S3CaptureOptions> options,
        Func<AWSCredentials, AmazonS3Config, IAmazonS3> clientFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clientFactory);
        _options = options.Value;
        _clientFactory = clientFactory;
    }

    public string Id => "s3";
    public string DisplayName => "S3 object storage";
    public PluginDefinition Definition => new("Archive one S3-compatible object, byte for byte.",
    [
        new("endpoint", "Endpoint", Required: true, HelpText: "HTTPS endpoint, without credentials or query parameters."),
        new("region", "Region", DefaultValue: "us-east-1"),
        new("bucket", "Bucket", Required: true),
        new("key", "Object key", Required: true),
        new("versionId", "Version ID", HelpText: "Optional fixed object version."),
        new("forcePathStyle", "Path-style addressing", "boolean", DefaultValue: "true"),
    ], "Worker secret JSON: {\"accessKeyId\":\"…\",\"secretAccessKey\":\"…\",\"sessionToken\":\"…\"}. Session token is optional.", true);

    public void ValidateConfiguration(string configurationJson, string bindingName, string? secretRef)
    {
        _ = ReadConfiguration(configurationJson);
        if (string.IsNullOrWhiteSpace(secretRef)) throw new PluginException("S3 requires a worker secret reference.");
    }

    private static S3BindingConfiguration ReadConfiguration(string json)
    {
        S3BindingConfiguration? config;
        try { config = JsonSerializer.Deserialize<S3BindingConfiguration>(json, JsonOptions); }
        catch (JsonException) { throw new PluginException("The S3 configuration is not valid JSON or contains unsupported fields."); }
        if (config is null || !Uri.TryCreate(config.Endpoint, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("https" or "http") || endpoint.UserInfo.Length != 0
            || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0
            || endpoint.AbsolutePath != "/")
            throw new PluginException("Enter an HTTP or HTTPS S3 endpoint without credentials, path, query or fragment.");
        if (string.IsNullOrWhiteSpace(config.Bucket) || config.Bucket.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-'))
            || string.IsNullOrWhiteSpace(config.Key) || string.IsNullOrWhiteSpace(config.Region))
            throw new PluginException("Enter a valid bucket, object key and region.");
        return config;
    }

    public async Task<PluginPayload> ExecuteAsync(PluginExecutionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var config = ReadConfiguration(context.Binding.ConfigurationJson);
        var endpoint = new Uri(config.Endpoint);
        if (!_options.AllowedHosts.Any(host => string.Equals(host.Trim(), endpoint.IdnHost, StringComparison.OrdinalIgnoreCase)))
            throw new PluginException("The S3 endpoint is not in Plugins:S3:AllowedHosts.");
        if (endpoint.Scheme == "http" && !_options.AllowInsecureHttp)
            throw new PluginException("S3 requires HTTPS unless the deployment explicitly permits HTTP.");
        S3Credentials? secret;
        try { secret = JsonSerializer.Deserialize<S3Credentials>(context.Secret ?? "", JsonOptions); }
        catch (JsonException) { throw new PluginException("The S3 worker secret must contain JSON credentials."); }
        if (secret is null || string.IsNullOrWhiteSpace(secret.AccessKeyId) || string.IsNullOrWhiteSpace(secret.SecretAccessKey))
            throw new PluginException("The S3 worker secret requires accessKeyId and secretAccessKey.");
        AWSCredentials credentials = string.IsNullOrEmpty(secret.SessionToken)
            ? new BasicAWSCredentials(secret.AccessKeyId, secret.SecretAccessKey)
            : new SessionAWSCredentials(secret.AccessKeyId, secret.SecretAccessKey, secret.SessionToken);
        using var client = _clientFactory(credentials, new AmazonS3Config
        {
            ServiceURL = config.Endpoint, AuthenticationRegion = config.Region,
            ForcePathStyle = config.ForcePathStyle, Timeout = _options.Timeout, MaxErrorRetry = 0,
            AllowAutoRedirect = false,
            HttpClientFactory = new S3SourceHttpClientFactory(endpoint, config.Bucket, config.ForcePathStyle, _options.Timeout),
        });
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_options.Timeout);
        try
        {
            using var response = await client.GetObjectAsync(new GetObjectRequest
            {
                BucketName = config.Bucket, Key = config.Key, VersionId = config.VersionId,
            }, deadline.Token);
            if (response.ContentLength > _options.MaxPayloadBytes)
                throw new PluginException("The S3 object exceeds the payload byte limit. Nothing was archived.");
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await response.ResponseStream.ReadAsync(chunk, deadline.Token)) != 0)
            {
                if (buffer.Length + read > _options.MaxPayloadBytes)
                    throw new PluginException("The S3 object exceeds the payload byte limit. Nothing was archived.");
                await buffer.WriteAsync(chunk.AsMemory(0, read), deadline.Token);
            }
            if (response.ContentLength >= 0 && response.ContentLength != buffer.Length)
                throw new PluginException("The S3 response was incomplete. Nothing was archived.");
            var url = $"s3://{config.Bucket}/{string.Join('/', config.Key.Split('/').Select(Uri.EscapeDataString))}";
            if (!string.IsNullOrEmpty(config.VersionId)) url += "?versionId=" + Uri.EscapeDataString(config.VersionId);
            return new PluginPayload
            {
                Url = url, Content = buffer.ToArray(), CapturedAtUtc = DateTimeOffset.UtcNow,
                MediaType = string.IsNullOrWhiteSpace(response.Headers.ContentType) ? "application/octet-stream" : response.Headers.ContentType,
            };
        }
        catch (AmazonS3Exception) { throw new PluginException("The S3 object could not be read. Check endpoint, credentials, bucket, key and version."); }
        catch (AmazonClientException) { throw new PluginException("The S3 endpoint could not be reached or authenticated."); }
    }
}

public sealed class S3BindingConfiguration
{
    public string Endpoint { get; set; } = "";
    public string Region { get; set; } = "us-east-1";
    public string Bucket { get; set; } = "";
    public string Key { get; set; } = "";
    public string? VersionId { get; set; }
    public bool ForcePathStyle { get; set; } = true;
}

internal sealed class S3Credentials
{
    public string AccessKeyId { get; set; } = "";
    public string SecretAccessKey { get; set; } = "";
    public string? SessionToken { get; set; }
}
