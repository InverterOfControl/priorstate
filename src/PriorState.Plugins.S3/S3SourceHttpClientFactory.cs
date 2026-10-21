using Amazon.Runtime;
using PriorState.Plugins.Abstractions;

namespace PriorState.Plugins.S3;

/// <summary>Checks every SDK transport request, including SDK-level regional redirects.</summary>
internal sealed class S3SourceHttpClientFactory(Uri endpoint, string bucket, bool pathStyle, TimeSpan timeout) : HttpClientFactory
{
    public override HttpClient CreateHttpClient(IClientConfig clientConfig) => new(
        new S3EndpointGuard(endpoint, bucket, pathStyle, new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            AutomaticDecompression = System.Net.DecompressionMethods.None,
        })) { Timeout = timeout };

    public override bool UseSDKHttpClientCaching(IClientConfig clientConfig) => true;
    public override bool DisposeHttpClientsAfterUse(IClientConfig clientConfig) => false;
    public override string? GetConfigUniqueString(IClientConfig clientConfig) => null;
}

internal sealed class S3EndpointGuard(Uri endpoint, string bucket, bool pathStyle, HttpMessageHandler inner) : DelegatingHandler(inner)
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var target = request.RequestUri;
        var expectedHost = pathStyle ? endpoint.IdnHost : bucket + "." + endpoint.IdnHost;
        if (target is null || target.Scheme != endpoint.Scheme || target.Port != endpoint.Port
            || !string.Equals(target.IdnHost, expectedHost, StringComparison.OrdinalIgnoreCase)
            || target.UserInfo.Length != 0)
            throw new PluginException("The S3 SDK attempted to send a request outside the configured source endpoint. Configure the correct endpoint directly.");
        return base.SendAsync(request, cancellationToken);
    }
}
