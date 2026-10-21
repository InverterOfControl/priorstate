using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PriorState.Plugins.Abstractions;

namespace PriorState.Plugins.S3;

public sealed class S3PluginModule : ICapturePluginModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<S3CaptureOptions>().Bind(configuration.GetSection("Plugins:S3"))
            .Validate(o => o.MaxPayloadBytes > 0 && o.MaxPayloadBytes <= int.MaxValue && o.Timeout > TimeSpan.Zero,
                "S3 payload limit and timeout must be positive.").ValidateOnStart();
        services.AddSingleton<ICapturePlugin, S3CapturePlugin>();
    }
}

public sealed class S3CaptureOptions
{
    public IList<string> AllowedHosts { get; } = [];
    public bool AllowInsecureHttp { get; set; }
    public long MaxPayloadBytes { get; set; } = 32 * 1024 * 1024;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
