using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PriorState.Plugins.Abstractions;

namespace PriorState.Plugins.Git;

public sealed class GitPluginModule : ICapturePluginModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<GitOptions>(configuration.GetSection("Plugins:Git"));
        services.AddSingleton<ICapturePlugin, GitCapturePlugin>();
    }
}

public sealed class GitOptions
{
    public IList<string> AllowedHosts { get; } = [];
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);
    public long MaxPayloadBytes { get; set; } = 32 * 1024 * 1024;
    public long MaxTemporaryBytes { get; set; } = 128 * 1024 * 1024;
    public int MaxFiles { get; set; } = 10000;
    public string GitExecutable { get; set; } = "git";
}
