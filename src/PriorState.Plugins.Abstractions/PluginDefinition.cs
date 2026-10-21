using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PriorState.Plugins.Abstractions;

/// <summary>Entry point invoked only at process startup, never while installing a package.</summary>
public interface ICapturePluginModule
{
    void Register(IServiceCollection services, IConfiguration configuration);
}

/// <summary>Non-secret binding fields. Types are string, number, boolean or multiline.</summary>
public sealed record PluginField(string Name, string Label, string Type = "string", bool Required = false,
    string? DefaultValue = null, string? HelpText = null);

public sealed record PluginDefinition(string Description, IReadOnlyList<PluginField> Fields,
    string SecretHelp = "", bool SecretRequired = false);
