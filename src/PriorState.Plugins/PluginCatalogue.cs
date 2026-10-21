using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Security.Cryptography;
using PriorState.Plugins.Abstractions;

namespace PriorState.Plugins;

/// <summary>
/// The plugins this build actually contains, and the versions they actually are.
///
/// The version is read from the assembly that is loaded, not from configuration and not from
/// anything the plugin declares about itself — the same rule the crawler follows when it reads
/// Chromium's version out of the running container rather than out of appsettings. A recorded
/// version that was copied from a config file attests to nothing.
/// </summary>
public sealed class PluginCatalogue
{
    private readonly Dictionary<string, RegisteredPlugin> _plugins;

    public PluginCatalogue(IEnumerable<ICapturePlugin> plugins, LoadedPluginPackages? packages = null)
    {
        ArgumentNullException.ThrowIfNull(plugins);

        _plugins = new Dictionary<string, RegisteredPlugin>(StringComparer.Ordinal);

        foreach (var plugin in plugins)
        {
            var identity = packages?.Identities.GetValueOrDefault(plugin.Id);
            if (packages is not null && identity is null)
                throw new PluginPackageException($"Plugin '{plugin.Id}' does not match its package manifest ID.");
            var hash = identity?.Sha256 ?? Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(plugin.GetType().Assembly.Location)));
            var registered = new RegisteredPlugin(plugin, ObservedVersion(plugin), identity?.Id ?? plugin.Id, hash);

            if (!_plugins.TryAdd(plugin.Id, registered))
            {
                throw new InvalidOperationException(
                    $"Two capture plugins are registered under the id '{plugin.Id}'. Plugin ids are recorded "
                    + "in the canonical form of every snapshot they produce and have to be unique.");
            }
        }
        if (packages is not null && _plugins.Count != packages.Identities.Count)
            throw new PluginPackageException("Loaded packages and registered plugins do not match.");
    }

    public IReadOnlyCollection<RegisteredPlugin> All => _plugins.Values;

    public bool TryGet(string pluginId, [NotNullWhen(true)] out RegisteredPlugin? plugin) =>
        _plugins.TryGetValue(pluginId, out plugin);

    private static string ObservedVersion(ICapturePlugin plugin)
    {
        var assembly = plugin.GetType().Assembly;

        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }
}

/// <summary>A plugin together with the version of the assembly it was loaded from.</summary>
public sealed record RegisteredPlugin(ICapturePlugin Plugin, string Version, string PackageId, string PackageSha256)
{
    public string Id => Plugin.Id;

    public string DisplayName => Plugin.DisplayName;
}
