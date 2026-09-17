using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PriorState.Plugins.Abstractions;

namespace PriorState.Plugins.HttpJson;

/// <summary>
/// Archives the response of an HTTP endpoint alongside the page capture.
///
/// The reference plugin, and the one the extension point was built for: a shop page quotes a
/// price, the price also lives in an ERP behind an API, and only having the page leaves "what did
/// your own system say at that moment" unanswerable.
///
/// The response is archived byte for byte. It is not parsed, reformatted or re-serialised — the
/// hash has to cover what the far end actually sent, not this plugin's rendering of it. JSON is
/// only validated when the response claims to be JSON, and even then the original bytes are what
/// gets stored.
/// </summary>
public sealed partial class HttpJsonCapturePlugin : ICapturePlugin
{
    /// <summary>Name of the resilience-wrapped client this plugin asks the factory for.</summary>
    public const string HttpClientName = "priorstate-plugin-http-json";

    private static readonly JsonSerializerOptions ConfigurationJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly HttpJsonOptions _options;
    private readonly ILogger<HttpJsonCapturePlugin> _logger;

    public HttpJsonCapturePlugin(
        IHttpClientFactory httpClientFactory,
        IOptions<HttpJsonOptions> options,
        ILogger<HttpJsonCapturePlugin> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;

        if (_options.AllowedHosts.Count == 0)
        {
            LogNoHostAllowlist();
        }
    }

    public string Id => "http-json";

    public string DisplayName => "HTTP endpoint";

    public async Task<PluginPayload> ExecuteAsync(
        PluginExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var configuration = ValidateConfiguration(context.Binding.ConfigurationJson, context.Binding.Designation);
        var uri = ValidateUrl(configuration.Url, context.Binding.Designation);

        // A plugin is a singleton, so it takes a client from the factory per execution rather than
        // holding one: a captured HttpClient never rotates its handler and pins DNS forever.
        using var http = _httpClientFactory.CreateClient(HttpClientName);
        http.Timeout = _options.Timeout;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_options.Timeout);

        using var request = BuildRequest(configuration, uri, context.Secret);
        using var response = await http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);

        if (!response.IsSuccessStatusCode)
        {
            // Deliberately not archived. A 500 page stored as though it were the data is worse
            // than no entry: the entry would look like a genuine record of what the API returned.
            throw new PluginException(
                $"HTTP {(int)response.StatusCode}. Nothing was archived. "
                + "For redirects, configure the destination URL directly.");
        }

        var content = await ReadBoundedAsync(response, uri, deadline.Token);
        var receivedAt = DateTimeOffset.UtcNow;
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? configuration.Accept;

        if (mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            EnsureParsesAsJson(content, uri);
        }

        LogFetched(uri, content.Length, mediaType);

        return new PluginPayload
        {
            CapturedAtUtc = receivedAt,
            Url = configuration.Url,
            FinalUrl = response.RequestMessage?.RequestUri is { } final && final != uri ? final.ToString() : null,
            MediaType = mediaType,
            Content = content,
        };
    }

    public static HttpJsonBindingConfiguration ValidateConfiguration(string configurationJson, string designation)
    {
        HttpJsonBindingConfiguration? configuration;

        try
        {
            configuration = JsonSerializer.Deserialize<HttpJsonBindingConfiguration>(
                configurationJson,
                ConfigurationJsonOptions);
        }
        catch (JsonException ex)
        {
            throw new PluginException($"The configuration of binding '{designation}' is not valid JSON.", ex);
        }

        if (configuration is null || !Uri.TryCreate(configuration.Url, UriKind.Absolute, out var uri))
        {
            throw new PluginException("Enter an absolute HTTP or HTTPS URL.");
        }
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            throw new PluginException("Only http and https URLs are supported.");
        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new PluginException("URLs are recorded in every evidence package. Use a secret header instead of credentials embedded in the URL.");
        if (!string.IsNullOrEmpty(uri.Fragment))
            throw new PluginException("Remove the URL fragment; it is not sent to the API.");
        if (configuration.Method is not ("GET" or "POST"))
            throw new PluginException("API sources support GET and POST only.");
        if (configuration.Headers is null)
            throw new PluginException("Headers must be an object.");
        if (configuration.AuthHeaderName is not null && (!IsHeaderName(configuration.AuthHeaderName)
            || configuration.AuthHeaderName.Equals("Host", StringComparison.OrdinalIgnoreCase)))
            throw new PluginException("Enter a valid authentication header name.");
        if (configuration.AuthValuePrefix?.IndexOfAny(['\r', '\n']) >= 0)
            throw new PluginException("The authentication prefix cannot contain line breaks.");
        foreach (var (name, value) in configuration.Headers)
        {
            if (!IsHeaderName(name) || value is null || value.IndexOfAny(['\r', '\n']) >= 0)
                throw new PluginException("Headers must have valid names and single-line values.");
            if (name.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Cookie", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Host", StringComparison.OrdinalIgnoreCase)
                || name.Equals(configuration.AuthHeaderName, StringComparison.OrdinalIgnoreCase))
                throw new PluginException("Use the secret reference for authentication headers; Host overrides are not supported.");
        }
        try
        {
            using var check = BuildRequest(configuration, uri, null);
            if (configuration.AuthHeaderName is { } authHeader && !check.Headers.TryAddWithoutValidation(authHeader, "validation"))
                throw new PluginException("The authentication header must be a request header, such as Authorization or X-API-Key.");
        }
        catch (FormatException ex)
        {
            throw new PluginException("Check the Accept and Content-Type headers.", ex);
        }
        return configuration;
    }

    private static bool IsHeaderName(string value) => value.Length > 0
        && value.All(c => char.IsAsciiLetterOrDigit(c) || "!#$%&'*+-.^_`|~".Contains(c));

    private Uri ValidateUrl(string url, string designation)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new PluginException($"Binding '{designation}' has no valid absolute URL configured.");
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new PluginException(
                $"Binding '{designation}' uses the scheme '{uri.Scheme}'. Only http and https are supported.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            // The URL is written into the canonical form and is therefore permanently readable in
            // every evidence package that snapshot ever produces.
            throw new PluginException(
                $"Binding '{designation}' has credentials embedded in its URL. The URL is recorded in the "
                + "canonical form and shipped in every evidence package, so it must not contain a secret. "
                + "Use a secret header instead.");
        }

        var allowedHosts = _options.AllowedHosts.Where(h => !string.IsNullOrWhiteSpace(h)).Select(h => h.Trim()).ToArray();
        if (allowedHosts.Length > 0
            && !allowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
        {
            throw new PluginException(
                $"Binding '{designation}' targets the host '{uri.Host}', which is not in "
                + "Plugins:HttpJson:AllowedHosts for this deployment.");
        }

        return uri;
    }

    private static HttpRequestMessage BuildRequest(
        HttpJsonBindingConfiguration configuration,
        Uri uri,
        string? secret)
    {
        var request = new HttpRequestMessage(new HttpMethod(configuration.Method), uri);

        try
        {
            request.Headers.Accept.ParseAdd(configuration.Accept);

            foreach (var (name, value) in configuration.Headers)
            {
                if (!request.Headers.TryAddWithoutValidation(name, value))
                    throw new PluginException("A configured header is not a request header. Set Content-Type in its dedicated field.");
            }

            if (!string.IsNullOrEmpty(configuration.AuthHeaderName) && secret is not null)
            {
                request.Headers.TryAddWithoutValidation(
                    configuration.AuthHeaderName,
                    (configuration.AuthValuePrefix ?? string.Empty) + secret);
            }

            if (configuration.Body is { Length: > 0 } body)
            {
                request.Content = new StringContent(body, Encoding.UTF8);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue(configuration.ContentType)
                {
                    CharSet = "utf-8",
                };
            }
        }
        catch
        {
            request.Dispose();
            throw;
        }

        return request;
    }

    /// <summary>
    /// Reads the response with a hard ceiling. The payload is buffered so it can be hashed, so an
    /// endpoint that streams without end must not be able to exhaust the worker.
    /// </summary>
    private async Task<byte[]> ReadBoundedAsync(
        HttpResponseMessage response,
        Uri uri,
        CancellationToken cancellationToken)
    {
        var limit = _options.MaxPayloadBytes;

        if (response.Content.Headers.ContentLength is { } declared && declared > limit)
        {
            throw new PluginException(
                $"{uri} declared {declared.ToString(CultureInfo.InvariantCulture)} bytes, over the "
                + $"{limit.ToString(CultureInfo.InvariantCulture)} byte limit for a plugin payload.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();

        var chunk = new byte[81920];
        int read;

        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > limit)
            {
                throw new PluginException(
                    $"{uri} returned more than the {limit.ToString(CultureInfo.InvariantCulture)} byte limit "
                    + "for a plugin payload. Nothing was archived.");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Confirms the bytes parse, then throws them away. Archiving a truncated or error-page
    /// response as though it were the data is the failure this prevents; the stored bytes are
    /// still the original ones.
    /// </summary>
    private static void EnsureParsesAsJson(byte[] content, Uri uri)
    {
        try
        {
            using var _ = JsonDocument.Parse(content);
        }
        catch (JsonException ex)
        {
            throw new PluginException(
                $"{uri} announced JSON but did not return valid JSON. Nothing was archived.", ex);
        }
    }

    [LoggerMessage(
        EventId = 6400,
        Level = LogLevel.Information,
        Message = "Fetched {Url}: {ByteCount} bytes of {MediaType}.")]
    private partial void LogFetched(Uri url, int byteCount, string mediaType);

    [LoggerMessage(
        EventId = 6401,
        Level = LogLevel.Warning,
        Message = "Plugins:HttpJson:AllowedHosts is empty, so a plugin binding may call any host reachable "
            + "from this container. Set an allowlist.")]
    private partial void LogNoHostAllowlist();
}
