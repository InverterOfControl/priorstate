using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace PriorState.Plugins;

public sealed class PluginPackageOptions
{
    public string Directory { get; set; } = Path.Combine(AppContext.BaseDirectory, "installed-plugins");
    public List<string> OperatorIds { get; set; } = [];
    public long MaxUploadBytes { get; set; } = 64 * 1024 * 1024;
}

public sealed record PluginManifest(int FormatVersion, int ContractVersion, string Id, string EntryAssembly, string EntryType);
public sealed record PluginPackageIdentity(string Id, string Sha256);
public sealed record PluginRuntimeState(DateTimeOffset StartedAt, DateTimeOffset SeenAt, IReadOnlyList<PluginPackageIdentity> Packages);

/// <summary>Installation only handles data. Assemblies are instantiated separately, at startup.</summary>
public sealed partial class PluginPackageStore : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly PluginPackageOptions _options;
    private readonly SemaphoreSlim _installation = new(1, 1);

    public PluginPackageStore(IOptions<PluginPackageOptions> options) => _options = options.Value;
    public PluginPackageOptions Options => _options;
    public void Dispose() => _installation.Dispose();

    public IReadOnlyList<PluginPackageIdentity> Installed => ReadActive();

    public static PluginManifest Inspect(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        if (archive.Entries.Count is 0 or > 512) throw new PluginPackageException("A package needs between 1 and 512 files.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expanded = 0;
        foreach (var entry in archive.Entries)
        {
            var path = entry.FullName;
            if (string.IsNullOrEmpty(path) || path.Length > 240 || path.Contains('\\') || path.Contains(':')
                || path.StartsWith('/') || path.Split('/').Any(UnsafeSegment)
                || !names.Add(path) || (entry.ExternalAttributes >> 16 & 0xF000) is not (0 or 0x8000))
                throw new PluginPackageException("Package paths must be unique relative file paths without links.");
            expanded = checked(expanded + entry.Length);
            if (expanded > 256L * 1024 * 1024) throw new PluginPackageException("Expanded package exceeds 256 MiB.");
        }
        var manifestEntry = archive.GetEntry("plugin.json") ?? throw new PluginPackageException("plugin.json is missing.");
        if (manifestEntry.Length > 16384) throw new PluginPackageException("Manifest is too large.");
        using var manifestStream = manifestEntry.Open();
        var manifest = JsonSerializer.Deserialize<PluginManifest>(manifestStream, Json)
            ?? throw new PluginPackageException("Invalid manifest.");
        if (manifest.FormatVersion != 1 || manifest.ContractVersion != 1)
            throw new PluginPackageException("Unsupported package or plugin contract version.");
        if (manifest.Id is null || !PluginIdPattern().IsMatch(manifest.Id)
            || string.IsNullOrWhiteSpace(manifest.EntryType) || manifest.EntryType.Length > 500
            || string.IsNullOrEmpty(manifest.EntryAssembly) || manifest.EntryAssembly.Contains('/')
            || !manifest.EntryAssembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || archive.GetEntry(manifest.EntryAssembly) is null)
            throw new PluginPackageException("Invalid plugin ID or entry point.");
        // Materialise every entry to detect damaged ZIPs and dishonest lengths before accepting it.
        foreach (var entry in archive.Entries)
        {
            using var input = entry.Open();
            var buffer = new byte[81920];
            long read = 0;
            int count;
            while ((count = input.Read(buffer)) != 0)
            {
                read += count;
                if (read > entry.Length) throw new PluginPackageException("Invalid expanded file size.");
            }
            if (read != entry.Length) throw new PluginPackageException("Truncated package file.");
        }
        return manifest;
    }

    public async Task<PluginPackageIdentity> InstallAsync(Stream input, CancellationToken ct,
        Func<PluginPackageIdentity, Task>? beforeActivate = null)
    {
        using var content = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await input.ReadAsync(buffer, ct)) != 0)
        {
            if (content.Length + count > _options.MaxUploadBytes) throw new PluginPackageException("Plugin upload is too large.");
            await content.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        var bytes = content.ToArray();
        PluginManifest manifest;
        try { manifest = Inspect(bytes); }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or OverflowException or ArgumentException)
        { throw new PluginPackageException("Invalid plugin package.", ex); }
        var identity = new PluginPackageIdentity(manifest.Id, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        await _installation.WaitAsync(ct);
        try
        {
            var packages = Path.Combine(_options.Directory, "packages");
            System.IO.Directory.CreateDirectory(packages);
            // A filesystem lock also serialises installations across API processes.
            await using var installationLock = new FileStream(Path.Combine(_options.Directory, "install.lock"),
                FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
            var destination = Path.Combine(packages, identity.Sha256 + ".psplugin");
            if (!File.Exists(destination)) await AtomicWriteAsync(destination, bytes, ct);
            else if (Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(destination, ct))) != identity.Sha256)
                throw new PluginPackageException("An existing package has been altered. Restore the package store before installation.");
            var active = ReadActive().Where(p => p.Id != identity.Id).Append(identity).OrderBy(p => p.Id, StringComparer.Ordinal).ToArray();
            if (beforeActivate is not null) await beforeActivate(identity);
            await AtomicWriteAsync(Path.Combine(_options.Directory, "active.json"), JsonSerializer.SerializeToUtf8Bytes(active, Json), ct);
            return identity;
        }
        finally { _installation.Release(); }
    }

    public IEnumerable<(PluginPackageIdentity Identity, byte[] Bytes)> StartupPackages()
    {
        var installed = ReadActive();
        var selected = new Dictionary<string, (PluginPackageIdentity, byte[])>(StringComparer.Ordinal);
        var bundled = Path.Combine(AppContext.BaseDirectory, "plugins");
        if (System.IO.Directory.Exists(bundled))
            foreach (var file in System.IO.Directory.EnumerateFiles(bundled, "*.psplugin").Order(StringComparer.Ordinal))
            {
                var bytes = File.ReadAllBytes(file);
                var manifest = Inspect(bytes);
                var identity = new PluginPackageIdentity(manifest.Id, Convert.ToHexStringLower(SHA256.HashData(bytes)));
                if (!selected.TryAdd(manifest.Id, (identity, bytes))) throw new PluginPackageException("Duplicate bundled plugin ID.");
            }
        foreach (var identity in installed)
        {
            var bytes = File.ReadAllBytes(Path.Combine(_options.Directory, "packages", identity.Sha256 + ".psplugin"));
            var manifest = Inspect(bytes);
            if (manifest.Id != identity.Id || Convert.ToHexStringLower(SHA256.HashData(bytes)) != identity.Sha256)
                throw new PluginPackageException("Installed package identity does not match its contents.");
            selected[identity.Id] = (identity, bytes);
        }
        return selected.Values;
    }

    public async Task WriteRuntimeAsync(string host, PluginRuntimeState state, CancellationToken ct)
    {
        var runtime = Path.Combine(_options.Directory, "runtime");
        System.IO.Directory.CreateDirectory(runtime);
        await AtomicWriteAsync(Path.Combine(runtime, host + ".json"), JsonSerializer.SerializeToUtf8Bytes(state, Json), ct);
    }

    public PluginRuntimeState? ReadRuntime(string host)
    {
        var file = Path.Combine(_options.Directory, "runtime", host + ".json");
        try
        {
            var state = File.Exists(file) ? JsonSerializer.Deserialize<PluginRuntimeState>(File.ReadAllBytes(file), Json) : null;
            return state?.Packages is not null && state.Packages.All(p => p is not null && p.Id is not null
                && PluginIdPattern().IsMatch(p.Id) && p.Sha256 is not null && HashPattern().IsMatch(p.Sha256)) ? state : null;
        }
        catch (IOException) { return null; }
        catch (JsonException) { return null; }
    }

    private PluginPackageIdentity[] ReadActive()
    {
        var file = Path.Combine(_options.Directory, "active.json");
        if (!File.Exists(file)) return [];
        var active = JsonSerializer.Deserialize<PluginPackageIdentity[]>(File.ReadAllBytes(file), Json)
            ?? throw new PluginPackageException("Invalid active plugin catalogue.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in active)
            if (p is null || p.Id is null || !PluginIdPattern().IsMatch(p.Id) || p.Sha256 is null
                || !HashPattern().IsMatch(p.Sha256) || !ids.Add(p.Id))
                throw new PluginPackageException("Invalid or duplicate active plugin identity.");
        return active;
    }

    private static async Task AtomicWriteAsync(string path, byte[] bytes, CancellationToken ct)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, ct);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    [GeneratedRegex("\\A[a-z][a-z0-9-]{0,63}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex PluginIdPattern();
    [GeneratedRegex("\\A[a-f0-9]{64}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex HashPattern();
    private static bool UnsafeSegment(string segment) => segment is "." or ".." or ""
        || segment.EndsWith('.') || segment.EndsWith(' ') || segment.Any(c => char.IsControl(c) || "<>\"|?*".Contains(c))
        || ReservedPathPattern().IsMatch(segment);
    [GeneratedRegex("^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])($|\\.)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReservedPathPattern();
}

public sealed class PluginPackageException : Exception
{
    public PluginPackageException(string message) : base(message) { }
    public PluginPackageException(string message, Exception inner) : base(message, inner) { }
}
