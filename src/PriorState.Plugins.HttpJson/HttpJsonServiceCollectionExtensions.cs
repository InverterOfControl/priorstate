using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PriorState.Plugins.Abstractions;

namespace PriorState.Plugins.HttpJson;

public static class HttpJsonServiceCollectionExtensions
{
    /// <summary>
    /// Registers the HTTP JSON capture plugin.
    ///
    /// Uses a single bounded request. Redirects and transparent retries are disabled so the
    /// worker does not forward credentials or repeat a POST without the operator asking.
    /// </summary>
    public static IServiceCollection AddHttpJsonCapturePlugin(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<HttpJsonOptions>()
            .Bind(configuration.GetSection(HttpJsonOptions.SectionName))
            .PostConfigure(options =>
            {
                var hosts = options.AllowedHosts.Where(h => !string.IsNullOrWhiteSpace(h))
                    .Select(h => h.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                options.AllowedHosts.Clear();
                foreach (var host in hosts) options.AllowedHosts.Add(host);
            });

        services.AddHttpClient(HttpJsonCapturePlugin.HttpClientName)
            // A redirect could leave the host allowlist or leak a custom authentication header.
            // Do not transparently retry POST requests: a test must issue exactly one request.
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

        // TryAddEnumerable directly rather than through PriorState.Plugins: a plugin depends on
        // the abstractions and nothing else, which is what makes the seam worth having.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICapturePlugin, HttpJsonCapturePlugin>());

        return services;
    }
}
