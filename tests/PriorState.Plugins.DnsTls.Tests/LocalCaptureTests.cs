using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PriorState.Domain.Entities;
using PriorState.Plugins.Abstractions;
using PriorState.Plugins.DnsTls;

namespace PriorState.Plugins.DnsTls.Tests;

public sealed class LocalCaptureTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(67108865)]
    public async Task RejectsInvalidPayloadLimitsBeforeNetwork(int limit)
    {
        var plugin = new DnsTlsCapturePlugin(Options.Create(new DnsTlsOptions { MaxPayloadBytes = limit }));
        var error = await Assert.ThrowsAsync<PluginException>(() => plugin.ExecuteAsync(Context("{\"host\":\"example.com\"}")));
        Assert.Contains("MaxPayloadBytes", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyOrWhitespaceHostAllowlistBlocksCapture()
    {
        var plugin = new DnsTlsCapturePlugin(Options.Create(new DnsTlsOptions { AllowedHosts = [" ", ""] }));
        var error = await Assert.ThrowsAsync<PluginException>(() => plugin.ExecuteAsync(Context("{\"host\":\"example.com\"}")));
        Assert.Contains("AllowedHosts", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplicitResolverRequiresAllowlist()
    {
        var plugin = new DnsTlsCapturePlugin(Options.Create(new DnsTlsOptions
        { AllowedHosts = ["example.com"], AllowedResolvers = [" ", ""] }));
        var error = await Assert.ThrowsAsync<PluginException>(() => plugin.ExecuteAsync(Context("{\"host\":\"example.com\",\"resolver\":\"127.0.0.1\"}")));
        Assert.Contains("AllowedResolvers", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2097152)]
    [InlineData(128)]
    public async Task CapturesSevenOriginalDnsResponsesFromConfiguredResolver(int maximumBytes)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var originals = new List<byte[]>();
        var server = Task.Run(async () =>
        {
            for (var i = 0; i < 7; i++)
            {
                using var client = await listener.AcceptTcpClientAsync(timeout.Token);
                await using var stream = client.GetStream();
                var prefix = new byte[2]; await stream.ReadExactlyAsync(prefix, timeout.Token);
                var message = new byte[BinaryPrimitives.ReadUInt16BigEndian(prefix)];
                await stream.ReadExactlyAsync(message, timeout.Token);
                message[2] = 129; message[3] = 128;
                originals.Add(message);
                await stream.WriteAsync(prefix, timeout.Token); await stream.WriteAsync(message, timeout.Token);
            }
        }, timeout.Token);
        var plugin = new DnsTlsCapturePlugin(Options.Create(new DnsTlsOptions
        { AllowedHosts = ["", "example.com"], AllowedResolvers = ["", "127.0.0.1"], MaxPayloadBytes = maximumBytes }));
        var context = Context(JsonSerializer.Serialize(new
        { host = "example.com", resolver = "127.0.0.1", resolverPort = port, captureTls = false }));
        if (maximumBytes == 128)
        {
            var error = await Assert.ThrowsAsync<PluginException>(() => plugin.ExecuteAsync(context, timeout.Token));
            Assert.Contains("MaxPayloadBytes", error.Message, StringComparison.Ordinal);
            await server;
            return;
        }
        var payload = await plugin.ExecuteAsync(context, timeout.Token);
        await server;
        using var document = JsonDocument.Parse(payload.Content);
        var entries = document.RootElement.GetProperty("dns").EnumerateArray().ToArray();
        Assert.Equal(new[] { 1, 28, 15, 16, 2, 6, 257 }, entries.Select(e => e.GetProperty("type").GetInt32()));
        for (var i = 0; i < 7; i++) Assert.Equal(originals[i], Convert.FromBase64String(entries[i].GetProperty("responseBase64").GetString()!));
    }

    [Fact]
    public async Task CapturesOriginalDerAndInvalidCertificateObservation()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-1));
        using var certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(timeout.Token);
            await using var ssl = new SslStream(client.GetStream());
            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate }, timeout.Token);
        }, timeout.Token);
        var plugin = new DnsTlsCapturePlugin(Options.Create(new DnsTlsOptions { AllowedHosts = ["localhost"] }));
        PluginPayload payload;
        try { payload = await plugin.ExecuteAsync(Context(JsonSerializer.Serialize(new { host = "localhost", port, captureDns = false })), timeout.Token); }
        catch { await server; throw; }
        await server;
        using var document = JsonDocument.Parse(payload.Content);
        var tls = document.RootElement.GetProperty("tls");
        Assert.Equal(certificate.RawData, Convert.FromBase64String(tls.GetProperty("leafDerBase64").GetString()!));
        Assert.Contains("RemoteCertificateChainErrors", tls.GetProperty("validationErrors").GetString(), StringComparison.Ordinal);
        Assert.False(tls.GetProperty("revocationChecked").GetBoolean());
    }

    private static PluginExecutionContext Context(string json) => new()
    {
        RunId = Guid.NewGuid(), ProjectId = Guid.NewGuid(), Profile = null!,
        Binding = new PluginBindingVersion { PluginId = "dns-tls", Name = "test", Version = 1,
            ConfigurationJson = json, Rationale = "test", Required = true },
    };
}


