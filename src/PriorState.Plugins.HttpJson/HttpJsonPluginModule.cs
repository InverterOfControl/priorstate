using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PriorState.Plugins.Abstractions;

namespace PriorState.Plugins.HttpJson;

public sealed class HttpJsonPluginModule : ICapturePluginModule
{
    public void Register(IServiceCollection services, IConfiguration configuration) =>
        services.AddHttpJsonCapturePlugin(configuration);
}
