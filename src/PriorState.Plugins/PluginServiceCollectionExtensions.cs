using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PriorState.Plugins.Abstractions;

namespace PriorState.Plugins;

/// <summary>
/// Registration for the capture plugin system.
///
/// Plugins are compiled in and registered explicitly rather than discovered from a directory at
/// startup. Loading arbitrary assemblies into the process that owns the ledger would mean the
/// answer to "what code produced this evidence" is no longer "the AGPL source you can read", which
/// is the thing PriorState asks an opposing party to rely on. The contract is shaped so that an
/// out-of-process or assembly-loading runner could be added later without changing it.
/// </summary>
public static class PluginServiceCollectionExtensions
{
    public static IServiceCollection AddPriorStatePlugins(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<PluginSecretResolver>();
        services.TryAddSingleton<PluginCatalogue>();
        services.TryAddScoped<PluginRunner>();

        return services;
    }

    /// <summary>
    /// Registers one capture plugin. Call after AddPriorStatePlugins, once per plugin.
    /// </summary>
    public static IServiceCollection AddCapturePlugin<TPlugin>(this IServiceCollection services)
        where TPlugin : class, ICapturePlugin
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICapturePlugin, TPlugin>());

        return services;
    }
}
