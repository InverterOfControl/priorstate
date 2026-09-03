using PriorState.Domain.Entities;
using PriorState.Plugins.Abstractions;

namespace PriorState.Plugins.Tests;

/// <summary>
/// Secrets live in the process environment and nowhere else. These tests pin the two behaviours
/// that keep that true: the naming rule that stops a binding reading arbitrary configuration out
/// of the worker's environment, and the refusal to substitute an empty string for a missing value.
/// </summary>
public sealed class PluginSecretResolverTests
{
    [Fact]
    public void ResolvesTheNamedEnvironmentVariable()
    {
        var resolver = Resolver(("PS_SECRET_TEST_TOKEN", "hunter2"));

        Assert.Equal("hunter2", resolver.Resolve(Binding("PS_SECRET_TEST_TOKEN")));
    }

    [Fact]
    public void ReturnsNullWhenTheBindingDeclaresNoSecret()
    {
        Assert.Null(Resolver().Resolve(Binding(null)));
    }

    [Fact]
    public void RefusesAVariableOutsideThePsSecretNamespace()
    {
        // Otherwise "configure a plugin" becomes "read any environment variable this container
        // has", which includes the database connection string.
        var resolver = Resolver(("ConnectionStrings__Postgres", "Host=db;Password=s3cret"));

        var ex = Assert.Throws<PluginException>(() =>
            resolver.Resolve(Binding("ConnectionStrings__Postgres")));

        Assert.Contains("PS_SECRET_", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FailsRatherThanSendingAnEmptySecretWhenTheVariableIsUnset()
    {
        // An empty Authorization header would archive an authentication failure page and record it
        // as though it were the data.
        var ex = Assert.Throws<PluginException>(() =>
            Resolver().Resolve(Binding("PS_SECRET_DEFINITELY_NOT_SET")));

        Assert.Contains("not set in this process", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("PS_SECRET_A", true)]
    [InlineData("PS_SECRET_ERP_TOKEN_2", true)]
    [InlineData("PS_SECRET_lowercase", false)]
    [InlineData("PS_SECRET_", false)]
    [InlineData("HOME", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ValidatesSecretNames(string? name, bool expected) =>
        Assert.Equal(expected, PluginSecretResolver.IsValidSecretRef(name));

    private static PluginSecretResolver Resolver(params (string Name, string Value)[] environment)
    {
        var values = environment.ToDictionary(e => e.Name, e => e.Value, StringComparer.Ordinal);
        return new PluginSecretResolver(name => values.GetValueOrDefault(name));
    }

    private static PluginBindingVersion Binding(string? secretRef) => new()
    {
        PluginId = "http-json",
        Name = "erp-prices",
        Version = 1,
        ConfigurationJson = "{}",
        SecretRef = secretRef,
        Rationale = "Test binding.",
        Required = false,
    };
}
