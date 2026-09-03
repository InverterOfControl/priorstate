using System.Text.RegularExpressions;
using PriorState.Domain.Entities;
using PriorState.Plugins.Abstractions;

namespace PriorState.Plugins;

/// <summary>
/// Resolves a binding's secret from the environment.
///
/// Secrets are referenced by the name of an environment variable and never stored. The database
/// holds the name, the canonical form holds the name, the evidence package holds the name — the
/// value exists only in the worker's process environment. That way exporting a package, dumping
/// the database or reading the ledger can none of them leak a credential, and the operator can
/// rotate one without producing a new binding version.
/// </summary>
public sealed partial class PluginSecretResolver
{
    /// <summary>
    /// The naming rule for secret variables. Enforced rather than merely documented: it keeps a
    /// binding from being pointed at an arbitrary variable in the worker's environment, which
    /// would turn "configure a plugin" into "read any of this container's configuration".
    /// </summary>
    [GeneratedRegex("^PS_SECRET_[A-Z0-9_]+$")]
    private static partial Regex SecretNamePattern { get; }

    private readonly Func<string, string?> _readEnvironmentVariable;

    public PluginSecretResolver()
        : this(Environment.GetEnvironmentVariable)
    {
    }

    /// <summary>
    /// Takes the environment read as a delegate so a deployment that keeps its secrets somewhere
    /// else can substitute one, and so tests do not have to mutate the process environment.
    /// </summary>
    public PluginSecretResolver(Func<string, string?> readEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(readEnvironmentVariable);
        _readEnvironmentVariable = readEnvironmentVariable;
    }

    public static bool IsValidSecretRef(string? secretRef) =>
        !string.IsNullOrEmpty(secretRef) && SecretNamePattern.IsMatch(secretRef);

    /// <summary>
    /// Returns the secret for a binding, or null if it declared none.
    ///
    /// An unresolvable reference is an error rather than an empty string: a plugin that silently
    /// sent an empty Authorization header would archive an authentication failure page and record
    /// it as though it were the data.
    /// </summary>
    public string? Resolve(PluginBindingVersion binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        if (string.IsNullOrEmpty(binding.SecretRef))
        {
            return null;
        }

        if (!SecretNamePattern.IsMatch(binding.SecretRef))
        {
            throw new PluginException(
                $"Binding '{binding.Designation}' references the secret '{binding.SecretRef}', which is not a "
                + "valid name. Secret variables must be named PS_SECRET_<NAME>, using capitals, digits and "
                + "underscores.");
        }

        var value = _readEnvironmentVariable(binding.SecretRef);

        if (string.IsNullOrEmpty(value))
        {
            throw new PluginException(
                $"Binding '{binding.Designation}' needs the environment variable {binding.SecretRef}, which is "
                + "not set in this process. Set it on the worker service and restart it; the value is never "
                + "stored in the database.");
        }

        return value;
    }
}
