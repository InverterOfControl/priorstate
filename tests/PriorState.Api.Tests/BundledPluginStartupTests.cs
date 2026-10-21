using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PriorState.Plugins;

namespace PriorState.Api.Tests;

public sealed class BundledPluginStartupTests
{
    [Fact]
    public void AllPublishedPackagesLoadWithoutStaticPluginReferences()
    {
        var directory = Path.Combine(Path.GetTempPath(), "priorstate-startup-test-" + Guid.NewGuid().ToString("N"));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Plugins:Directory"] = directory }).Build();
        var services = new ServiceCollection().AddPriorStatePlugins().AddPluginPackages(configuration, "api");
        using var provider = services.BuildServiceProvider();
        var catalogue = provider.GetRequiredService<PluginCatalogue>();
        Assert.Equal(["dns-tls", "git", "http-json", "s3"], catalogue.All.Select(p => p.Id).Order(StringComparer.Ordinal).ToArray());
        foreach (var plugin in catalogue.All)
        {
            Assert.Equal(64, plugin.PackageSha256.Length);
            Assert.NotEmpty(plugin.Plugin.Definition.Description);
            Assert.NotEqual(System.Runtime.Loader.AssemblyLoadContext.Default,
                System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(plugin.Plugin.GetType().Assembly));
        }
        catalogue.TryGet("s3", out var s3);
        var context = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(s3!.Plugin.GetType().Assembly)!;
        var sdk = context.LoadFromAssemblyName(new System.Reflection.AssemblyName("AWSSDK.S3"));
        Assert.Same(context, System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(sdk));
        Assert.NotSame(typeof(Amazon.S3.AmazonS3Client).Assembly, sdk);
        s3!.Plugin.ValidateConfiguration("""{"endpoint":"https://objects.example.test","region":"us-east-1","bucket":"documents","key":"prices.pdf"}""", "Documents", "PS_SECRET_S3");
        catalogue.TryGet("dns-tls", out var dns);
        dns!.Plugin.ValidateConfiguration("""{"host":"example.test"}""", "DNS", null);
        catalogue.TryGet("git", out var git);
        git!.Plugin.ValidateConfiguration("""{"url":"https://git.example.test/repo.git"}""", "Git", null);
    }
}
