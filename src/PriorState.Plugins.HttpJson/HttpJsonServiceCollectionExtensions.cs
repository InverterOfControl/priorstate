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
    /// Uses AddHttpClient with the standard resilience handler, the same way the timestamp
    /// authority client is registered: a transient failure reaching an ERP should be retried
    /// before it becomes a recorded plugin failure.
    /// </summary>
    public static IServiceCollection AddHttpJsonCapturePlugin(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<HttpJsonOptions>()
            .Bind(configuration.GetSection(HttpJsonOptions.SectionName));

        services.AddHttpClient(HttpJsonCapturePlugin.HttpClientName)
            .AddStandardResilienceHandler();

        // TryAddEnumerable directly rather than through PriorState.Plugins: a plugin depends on
        // the abstractions and nothing else, which is what makes the seam worth having.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICapturePlugin, HttpJsonCapturePlugin>());

        return services;
    }
}
