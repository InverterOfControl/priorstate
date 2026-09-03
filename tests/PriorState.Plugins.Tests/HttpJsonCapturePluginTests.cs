using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PriorState.Domain.Entities;
using PriorState.Domain.ValueObjects;
using PriorState.Plugins.Abstractions;
using PriorState.Plugins.HttpJson;

namespace PriorState.Plugins.Tests;

public sealed class HttpJsonCapturePluginTests
{
    [Fact]
    public async Task ArchivesTheResponseBytesExactlyAsReceived()
    {
        // Whitespace and key order included. The hash has to cover what the far end sent, not this
        // plugin's idea of how that should look.
        const string body = "{\n  \"sku\": \"A-1\",\n  \"price\":   19.99\n}";
        var plugin = Plugin(Responder(body, "application/json"));

        var payload = await plugin.ExecuteAsync(Context());

        Assert.Equal(body, Encoding.UTF8.GetString(payload.Content));
        Assert.Equal("application/json", payload.MediaType);
    }

    [Fact]
    public async Task SendsTheSecretInTheConfiguredHeaderAndNowhereElse()
    {
        HttpRequestMessage? seen = null;
        var plugin = Plugin(request =>
        {
            seen = request;
            return Respond("{}", "application/json");
        });

        await plugin.ExecuteAsync(Context(secret: "hunter2"));

        Assert.Equal("Bearer hunter2", seen!.Headers.GetValues("Authorization").Single());
        Assert.DoesNotContain("hunter2", seen.RequestUri!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ArchivesNothingWhenTheEndpointFails()
    {
        // A 500 body stored as though it were the data would look like a genuine record of what
        // the API returned. Refusing is the whole point.
        var plugin = Plugin(_ => Respond("upstream is down", "text/html", HttpStatusCode.InternalServerError));

        var ex = await Assert.ThrowsAsync<PluginException>(() => plugin.ExecuteAsync(Context()));

        Assert.Contains("500", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Nothing was archived", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ArchivesNothingWhenJsonIsTruncated()
    {
        var plugin = Plugin(Responder("{\"sku\": \"A-1\"", "application/json"));

        var ex = await Assert.ThrowsAsync<PluginException>(() => plugin.ExecuteAsync(Context()));

        Assert.Contains("did not return valid JSON", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesAHostThatIsNotOnTheAllowlist()
    {
        // The worker container mounts the Docker socket, so an unrestricted fetch from inside it is
        // a genuine escalation path rather than a theoretical one.
        var plugin = Plugin(Responder("{}", "application/json"), allowedHosts: ["erp.internal"]);

        var ex = await Assert.ThrowsAsync<PluginException>(() =>
            plugin.ExecuteAsync(Context(url: "http://169.254.169.254/latest/meta-data/")));

        Assert.Contains("AllowedHosts", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AllowsAHostThatIsOnTheAllowlist()
    {
        var plugin = Plugin(Responder("{}", "application/json"), allowedHosts: ["erp.internal"]);

        var payload = await plugin.ExecuteAsync(Context(url: "https://erp.internal/api/prices"));

        Assert.Equal("https://erp.internal/api/prices", payload.Url);
    }

    [Fact]
    public async Task RefusesCredentialsEmbeddedInTheUrl()
    {
        // The URL is written into the canonical form and is readable in every evidence package
        // that snapshot ever produces.
        var plugin = Plugin(Responder("{}", "application/json"));

        var ex = await Assert.ThrowsAsync<PluginException>(() =>
            plugin.ExecuteAsync(Context(url: "https://user:pass@erp.internal/api/prices")));

        Assert.Contains("evidence package", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesANonHttpScheme()
    {
        var plugin = Plugin(Responder("{}", "application/json"));

        var ex = await Assert.ThrowsAsync<PluginException>(() =>
            plugin.ExecuteAsync(Context(url: "file:///etc/passwd")));

        Assert.Contains("Only http and https", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesAResponseOverTheSizeLimit()
    {
        var plugin = Plugin(Responder(new string('x', 5000), "text/plain"), maxPayloadBytes: 1024);

        var ex = await Assert.ThrowsAsync<PluginException>(() => plugin.ExecuteAsync(Context()));

        Assert.Contains("limit", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportsTheFinalUrlWhenTheRequestWasRedirected()
    {
        var plugin = Plugin(request =>
        {
            var response = Respond("{}", "application/json");
            response.RequestMessage = new HttpRequestMessage(
                HttpMethod.Get, new Uri("https://erp.internal/api/prices/v2"));
            return response;
        });

        var payload = await plugin.ExecuteAsync(Context(url: "https://erp.internal/api/prices"));

        Assert.Equal("https://erp.internal/api/prices/v2", payload.FinalUrl);
    }

    // --- helpers ---

    private static Func<HttpRequestMessage, HttpResponseMessage> Responder(string body, string mediaType) =>
        _ => Respond(body, mediaType);

    private static HttpResponseMessage Respond(
        string body,
        string mediaType,
        HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, mediaType),
    };

    private static HttpJsonCapturePlugin Plugin(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        string[]? allowedHosts = null,
        long maxPayloadBytes = 32 * 1024 * 1024)
    {
        var options = new HttpJsonOptions { MaxPayloadBytes = maxPayloadBytes };

        foreach (var host in allowedHosts ?? [])
        {
            options.AllowedHosts.Add(host);
        }

        return new HttpJsonCapturePlugin(
            new StubHttpClientFactory(responder),
            Options.Create(options),
            NullLogger<HttpJsonCapturePlugin>.Instance);
    }

    private static PluginExecutionContext Context(
        string url = "https://erp.internal/api/prices",
        string? secret = null) => new()
        {
            RunId = Guid.CreateVersion7(),
            ProjectId = Guid.CreateVersion7(),
            Profile = new CaptureProfileVersion
            {
                Name = "DE-Standard",
                Version = 1,
                Rationale = "Baseline.",
                Conditions = new CaptureConditions
                {
                    UserAgent = "Mozilla/5.0",
                    ViewportWidth = 1920,
                    ViewportHeight = 1080,
                    AuthenticatedSession = false,
                    AdBlockerActive = false,
                    CookieBanner = CookieBannerHandling.LeftAsIs,
                    JavaScriptSettleMs = 2000,
                    ChromiumVersion = "140.0.7259.68",
                    CrawlerVersion = "1.7.1",
                },
            },
            Binding = new PluginBindingVersion
            {
                PluginId = "http-json",
                Name = "erp-prices",
                Version = 1,
                ConfigurationJson =
                    $$"""
                    {"url":"{{url}}","method":"GET","authHeaderName":"Authorization","authValuePrefix":"Bearer "}
                    """,
                SecretRef = "PS_SECRET_ERP_TOKEN",
                Rationale = "Test binding.",
                Required = false,
            },
            Secret = secret,
        };

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
            _responder = responder;

        public HttpClient CreateClient(string name) => new(new StubHandler(_responder));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = _responder(request);
            response.RequestMessage ??= request;
            return Task.FromResult(response);
        }
    }
}
