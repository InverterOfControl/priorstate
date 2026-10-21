using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PriorState.Plugins.HttpJson;

namespace PriorState.Plugins.Tests;

public sealed class PluginPackageTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "priorstate-package-tests-" + Guid.NewGuid().ToString("N"));
    private PluginPackageStore Store(long maxBytes = 64 * 1024 * 1024) => new(Options.Create(new PluginPackageOptions
    { Directory = _directory, MaxUploadBytes = maxBytes }));

    [Fact]
    public async Task InstallationDoesNotLoadCodeAndKeepsPreviousPackages()
    {
        using var store = Store();
        var firstBytes = Package(); // The DLL is deliberately not an assembly: installation must not execute it.
        using var first = new MemoryStream(firstBytes);
        var identity = await store.InstallAsync(first, CancellationToken.None);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(firstBytes)), identity.Sha256);
        Assert.Equal(identity, Assert.Single(store.Installed));
        using var second = new MemoryStream(Package(extraFile: "readme.txt"));
        var updated = await store.InstallAsync(second, CancellationToken.None);
        Assert.NotEqual(identity.Sha256, updated.Sha256);
        Assert.Equal(updated, Assert.Single(store.Installed));
        Assert.True(File.Exists(Path.Combine(_directory, "packages", identity.Sha256 + ".psplugin")));
    }

    [Theory]
    [InlineData("../escape.dll")]
    [InlineData("/escape.dll")]
    [InlineData("C:/escape.dll")]
    [InlineData("folder\\escape.dll")]
    [InlineData("folder/../escape.dll")]
    [InlineData("folder/.. /escape.dll")]
    [InlineData("CON.dll")]
    [InlineData("folder./file.dll")]
    [InlineData("plugin.json")]
    public async Task RejectsUnsafeOrDuplicatePaths(string extra)
    {
        using var store = Store();
        using var input = new MemoryStream(Package(extraFile: extra));
        await Assert.ThrowsAsync<PluginPackageException>(() => store.InstallAsync(input, CancellationToken.None));
        Assert.Empty(store.Installed);
    }

    [Fact]
    public async Task RejectsSymlinksInPackages()
    {
        var bytes = Package(extraFile: "link");
        using var stream = new MemoryStream();
        stream.Write(bytes);
        stream.Position = 0;
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
            zip.GetEntry("link")!.ExternalAttributes = 0xA000 << 16;
        using var store = Store();
        stream.Position = 0;
        await Assert.ThrowsAsync<PluginPackageException>(() => store.InstallAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task RejectsUnsupportedContractsAndOversizedUploads()
    {
        using var store = Store();
        using var unsupported = new MemoryStream(Package(contract: 99));
        await Assert.ThrowsAsync<PluginPackageException>(() => store.InstallAsync(unsupported, CancellationToken.None));
        using var small = Store(10);
        using var tooLarge = new MemoryStream(Package());
        await Assert.ThrowsAsync<PluginPackageException>(() => small.InstallAsync(tooLarge, CancellationToken.None));
    }

    [Fact]
    public async Task AuditFailureCannotActivatePackage()
    {
        using var store = Store();
        using var input = new MemoryStream(Package());
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.InstallAsync(input, CancellationToken.None,
            _ => throw new InvalidOperationException("Audit unavailable")));
        Assert.Empty(store.Installed);
    }

    [Theory]
    [InlineData("http-json\n")]
    [InlineData("../http-json")]
    [InlineData("HTTP-JSON")]
    public async Task InvalidManifestIdsAreRejected(string id)
    {
        using var store = Store();
        using var package = new MemoryStream(Package(id: id));
        await Assert.ThrowsAsync<PluginPackageException>(() => store.InstallAsync(package, CancellationToken.None));
    }

    [Fact]
    public async Task TamperedPackageIsRejectedBeforeLoading()
    {
        using var store = Store();
        using var input = new MemoryStream(Package());
        var identity = await store.InstallAsync(input, CancellationToken.None);
        File.WriteAllBytes(Path.Combine(_directory, "packages", identity.Sha256 + ".psplugin"), Package(extraFile: "changed.txt"));
        Assert.Throws<PluginPackageException>(() => store.StartupPackages().ToArray());
        using var reinstall = new MemoryStream(Package());
        await Assert.ThrowsAsync<PluginPackageException>(() => store.InstallAsync(reinstall, CancellationToken.None));
    }

    [Fact]
    public async Task LoadedPluginRemainsPinnedUntilNextStartup()
    {
        using var store = Store();
        using var first = new MemoryStream(Package(realAssembly: true));
        var original = await store.InstallAsync(first, CancellationToken.None);
        var configuration = new ConfigurationBuilder().Build();
        using var loaded = new LoadedPluginPackages();
        var services = new ServiceCollection().AddPriorStatePlugins();
        loaded.Load(services, configuration, store);
        services.AddSingleton(loaded);
        using var provider = services.BuildServiceProvider();
        var catalogue = provider.GetRequiredService<PluginCatalogue>();
        var plugin = Assert.Single(catalogue.All);
        Assert.Equal(original.Sha256, plugin.PackageSha256);
        Assert.NotSame(typeof(HttpJsonCapturePlugin).Assembly, plugin.Plugin.GetType().Assembly);
        using var second = new MemoryStream(Package(realAssembly: true, extraFile: "readme.txt"));
        var update = await store.InstallAsync(second, CancellationToken.None);
        Assert.Equal(original.Sha256, plugin.PackageSha256);
        using var restarted = new LoadedPluginPackages();
        restarted.Load(new ServiceCollection(), configuration, store);
        Assert.Equal(update.Sha256, restarted.Identities["http-json"].Sha256);
    }

    [Fact]
    public async Task DuplicateRegisteredIdsFailStartup()
    {
        using var store = Store();
        using var input = new MemoryStream(Package(realAssembly: true));
        await store.InstallAsync(input, CancellationToken.None);
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection().AddPriorStatePlugins().AddHttpJsonCapturePlugin(config);
        using var loaded = new LoadedPluginPackages();
        loaded.Load(services, config, store);
        services.AddSingleton(loaded);
        using var provider = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<PluginCatalogue>());
    }

    private static byte[] Package(string? extraFile = null, int contract = 1, bool realAssembly = false, string id = "http-json")
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add("plugin.json", JsonSerializer.SerializeToUtf8Bytes(new
            {
                formatVersion = 1, contractVersion = contract, id,
                entryAssembly = "PriorState.Plugins.HttpJson.dll", entryType = "PriorState.Plugins.HttpJson.HttpJsonPluginModule",
            }));
            Add("PriorState.Plugins.HttpJson.dll", realAssembly ? File.ReadAllBytes(typeof(HttpJsonCapturePlugin).Assembly.Location) : "not executable"u8.ToArray());
            if (extraFile is not null) Add(extraFile, "extra"u8.ToArray());
            void Add(string name, byte[] bytes)
            {
                using var content = archive.CreateEntry(name).Open();
                content.Write(bytes);
            }
        }
        return memory.ToArray();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
