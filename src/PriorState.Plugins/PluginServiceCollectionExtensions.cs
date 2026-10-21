using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PriorState.Plugins.Abstractions;

namespace PriorState.Plugins;

/// <summary>
/// Registration for the capture plugin system.
///
/// Packages are installed by trusted operators and loaded at startup. Package hashes identify
/// the executed code in new ledger entries; loading contexts isolate dependencies, not privileges.
/// </summary>
public static class PluginServiceCollectionExtensions
{
    public static IServiceCollection AddPluginPackages(this IServiceCollection services,
        IConfiguration configuration, string host)
    {
        var options = new PluginPackageOptions();
        configuration.GetSection("Plugins").Bind(options);
        if (options.MaxUploadBytes is <= 0 or > 256L * 1024 * 1024)
            throw new PluginPackageException("Plugins:MaxUploadBytes must be between 1 and 256 MiB.");
        var store = new PluginPackageStore(Options.Create(options));
        var loaded = new LoadedPluginPackages();
        try { loaded.Load(services, configuration, store); }
        catch
        {
            loaded.Dispose();
            store.Dispose();
            throw;
        }
        services.AddSingleton<PluginPackageStore>(_ => store);
        services.AddSingleton<LoadedPluginPackages>(_ => loaded);
        services.AddSingleton<IHostedService>(provider => new PluginRuntimeReporter(store,
            provider.GetRequiredService<PluginCatalogue>(), provider.GetRequiredService<ILogger<PluginRuntimeReporter>>(), host));
        return services;
    }

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
