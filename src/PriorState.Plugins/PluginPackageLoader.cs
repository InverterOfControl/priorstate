using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PriorState.Plugins.Abstractions;

namespace PriorState.Plugins;

public sealed class LoadedPluginPackages : IDisposable
{
    private readonly List<string> _temporaryDirectories = [];
    private readonly List<AssemblyLoadContext> _contexts = [];
    private readonly Dictionary<string, PluginPackageIdentity> _identities = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, PluginPackageIdentity> Identities => _identities;

    public void Load(IServiceCollection services, IConfiguration configuration, PluginPackageStore store)
    {
        foreach (var (identity, bytes) in store.StartupPackages())
        {
            var manifest = PluginPackageStore.Inspect(bytes);
            var directory = Path.Combine(Path.GetTempPath(), "priorstate-plugins", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            _temporaryDirectories.Add(directory);
            using (var stream = new MemoryStream(bytes, writable: false))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read)) zip.ExtractToDirectory(directory);
            var context = new PluginLoadContext(directory, manifest.EntryAssembly);
            _contexts.Add(context);
            var assembly = context.LoadFromAssemblyPath(Path.Combine(directory, manifest.EntryAssembly));
            var type = assembly.GetType(manifest.EntryType, throwOnError: true)!;
            if (!typeof(ICapturePluginModule).IsAssignableFrom(type) || type.IsAbstract)
                throw new PluginPackageException("Package entry type must implement ICapturePluginModule.");
            var before = services.Count;
            ((ICapturePluginModule)Activator.CreateInstance(type)!).Register(services, configuration);
            var registrations = services.Skip(before).Where(d => d.ServiceType == typeof(ICapturePlugin)).ToList();
            if (registrations.Count != 1 || registrations[0].ImplementationType?.Assembly != assembly)
                throw new PluginPackageException("A package must register exactly one capture plugin from its entry assembly.");
            _identities.Add(manifest.Id, identity);
        }
    }

    public void Dispose()
    {
        foreach (var context in _contexts) context.Unload();
        foreach (var directory in _temporaryDirectories)
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { /* Windows may retain a loaded assembly until process exit. */ }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed class PluginLoadContext(string directory, string entryAssembly) : AssemblyLoadContext(isCollectible: true)
    {
        private readonly AssemblyDependencyResolver _resolver = new(Path.Combine(directory, entryAssembly));

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var name = assemblyName.Name ?? "";
            if (name is "PriorState.Plugins.Abstractions" or "PriorState.Domain"
                || name.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal)
                || name.StartsWith("System.", StringComparison.Ordinal) || name == "netstandard")
            {
                try { return Default.LoadFromAssemblyName(assemblyName); }
                catch (FileNotFoundException) when (name is not ("PriorState.Plugins.Abstractions" or "PriorState.Domain"))
                { /* Optional System/Microsoft packages can be supplied by a plugin. */ }
            }
            var path = _resolver.ResolveAssemblyToPath(assemblyName) ?? Path.Combine(directory, name + ".dll");
            return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
        }

        protected override nint LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path is null ? 0 : LoadUnmanagedDllFromPath(path);
        }
    }
}

public sealed class PluginRuntimeReporter(PluginPackageStore store, PluginCatalogue catalogue,
    ILogger<PluginRuntimeReporter> logger, string host) : BackgroundService
{
    private static readonly Action<ILogger, Exception?> ReportFailure = LoggerMessage.Define(LogLevel.Warning,
        new EventId(6100, "PluginRuntimeStatus"), "Could not publish plugin runtime status.");
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var started = DateTimeOffset.UtcNow;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                await store.WriteRuntimeAsync(host, new(started, DateTimeOffset.UtcNow,
                    catalogue.All.Select(p => new PluginPackageIdentity(p.Id, p.PackageSha256)).ToArray()), stoppingToken);
            }
            catch (IOException ex) { ReportFailure(logger, ex); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
