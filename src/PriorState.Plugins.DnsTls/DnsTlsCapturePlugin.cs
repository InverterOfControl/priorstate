using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PriorState.Plugins.Abstractions;

namespace PriorState.Plugins.DnsTls;

public sealed class DnsTlsPluginModule : ICapturePluginModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DnsTlsOptions>(configuration.GetSection("Plugins:DnsTls"));
        services.AddSingleton<ICapturePlugin, DnsTlsCapturePlugin>();
    }
}

public sealed class DnsTlsOptions
{
    public List<string> AllowedHosts { get; set; } = [];
    public List<string> AllowedResolvers { get; set; } = [];
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    public int MaxPayloadBytes { get; set; } = 2 * 1024 * 1024;
}

public sealed record DnsTlsConfiguration(string Host, int Port = 443, string? Resolver = null,
    int ResolverPort = 53, bool CaptureDns = true, bool CaptureTls = true);

/// <summary>Records DNS messages and TLS observations in a generated, versioned JSON envelope.</summary>
public sealed class DnsTlsCapturePlugin(IOptions<DnsTlsOptions> options) : ICapturePlugin
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly ushort[] QueryTypes = [1, 28, 15, 16, 2, 6, 257];
    public string Id => "dns-tls";
    public string DisplayName => "DNS and TLS";
    public PluginDefinition Definition => new("Archive DNS records, original DNS messages and the observed TLS certificate.",
    [new("host", "Host", Required: true), new("port", "TLS port", "number", DefaultValue: "443"),
     new("resolver", "DNS resolver IP", HelpText: "Leave empty to use the worker's first configured DNS resolver."),
     new("resolverPort", "DNS resolver port", "number", DefaultValue: "53"),
     new("captureDns", "Capture DNS", "boolean", DefaultValue: "true"),
     new("captureTls", "Capture TLS", "boolean", DefaultValue: "true")]);

    public void ValidateConfiguration(string configurationJson, string bindingName, string? secretRef)
    {
        _ = Parse(configurationJson);
        if (!string.IsNullOrEmpty(secretRef)) throw new PluginException("DNS/TLS does not use credentials.");
    }

    private static DnsTlsConfiguration Parse(string json)
    {
        DnsTlsConfiguration? config;
        try { config = JsonSerializer.Deserialize<DnsTlsConfiguration>(json, JsonOptions); }
        catch (JsonException ex) { throw new PluginException("Invalid DNS/TLS configuration JSON.", ex); }
        if (config is null || string.IsNullOrWhiteSpace(config.Host)
            || Uri.CheckHostName(config.Host) == UriHostNameType.Unknown
            || config.Host.Length > 253 || config.Host.Contains(':', StringComparison.Ordinal)
            || config.Port is < 1 or > 65535 || config.ResolverPort is < 1 or > 65535
            || (!config.CaptureDns && !config.CaptureTls)
            || (config.Resolver is not null && !IPAddress.TryParse(config.Resolver, out _)))
            throw new PluginException("Enter a valid host, valid ports, an optional resolver IP and enable at least one capture.");
        if (config.CaptureDns) _ = DnsWire.CreateQuery(config.Host, 1);
        return config;
    }

    public async Task<PluginPayload> ExecuteAsync(PluginExecutionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var config = Parse(context.Binding.ConfigurationJson);
        var policy = options.Value;
        if (policy.MaxPayloadBytes is < 1 or > 64 * 1024 * 1024)
            throw new PluginException("Plugins:DnsTls:MaxPayloadBytes must be between 1 byte and 64 MiB.");
        var allowedHosts = policy.AllowedHosts.Where(h => !string.IsNullOrWhiteSpace(h)).Select(h => h.Trim()).ToArray();
        if (!allowedHosts.Contains(config.Host, StringComparer.OrdinalIgnoreCase))
            throw new PluginException("The host is not in Plugins:DnsTls:AllowedHosts.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(policy.Timeout);
        var dns = new List<object>();
        string? resolver = null;
        if (config.CaptureDns)
        {
            resolver = config.Resolver ?? NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up)
                .SelectMany(n => n.GetIPProperties().DnsAddresses).FirstOrDefault()?.ToString()
                ?? throw new PluginException("No DNS resolver is configured on the worker. Set a resolver IP.");
            var allowedResolvers = policy.AllowedResolvers.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).ToArray();
            if ((allowedResolvers.Length > 0 || config.Resolver is not null)
                && !allowedResolvers.Contains(resolver, StringComparer.OrdinalIgnoreCase))
                throw new PluginException("The resolver is not in Plugins:DnsTls:AllowedResolvers.");
            foreach (var type in QueryTypes)
            {
                var query = DnsWire.CreateQuery(config.Host, type);
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Parse(resolver), config.ResolverPort, deadline.Token);
                await using var stream = client.GetStream();
                var prefix = new byte[2];
                BinaryPrimitives.WriteUInt16BigEndian(prefix, checked((ushort)query.Length));
                await stream.WriteAsync(prefix, deadline.Token);
                await stream.WriteAsync(query, deadline.Token);
                await stream.ReadExactlyAsync(prefix, deadline.Token);
                var response = new byte[BinaryPrimitives.ReadUInt16BigEndian(prefix)];
                await stream.ReadExactlyAsync(response, deadline.Token);
                var parsed = DnsWire.ParseResponse(query, response);
                dns.Add(new { type, transport = "tcp", observedAtUtc = DateTimeOffset.UtcNow,
                    requestBase64 = Convert.ToBase64String(query), responseBase64 = Convert.ToBase64String(response),
                    parsed.ResponseCode, parsed.Records });
            }
        }
        object? tls = null;
        if (config.CaptureTls)
        {
            using var client = new TcpClient();
            await client.ConnectAsync(config.Host, config.Port, deadline.Token);
            var certificates = new List<string>();
            var statuses = new List<string>();
            string? leaf = null;
            string? errors = null;
            await using var ssl = new SslStream(client.GetStream(), false, (_, certificate, chain, policyErrors) =>
            {
                var certificateBytes = certificate?.GetRawCertData();
                var chainByteCount = chain?.ChainElements.Sum(e => (long)e.Certificate.RawData.Length) ?? 0;
                if ((certificateBytes?.LongLength ?? 0) + chainByteCount > policy.MaxPayloadBytes)
                    throw new PluginException("TLS certificate material exceeds Plugins:DnsTls:MaxPayloadBytes.");
                leaf = certificateBytes is null ? null : Convert.ToBase64String(certificateBytes);
                errors = policyErrors.ToString();
                if (chain is not null)
                {
                    certificates.AddRange(chain.ChainElements.Select(e => Convert.ToBase64String(e.Certificate.RawData)));
                    statuses.AddRange(chain.ChainStatus.Select(s => s.Status.ToString()));
                }
                return true; // Observation only: no credentials or application data are sent.
            });
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = config.Host,
                CertificateChainPolicy = new X509ChainPolicy { RevocationMode = X509RevocationMode.NoCheck, DisableCertificateDownloads = true },
            }, deadline.Token);
            if (leaf is null) throw new PluginException("TLS peer presented no certificate.");
            using var observedCertificate = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(leaf));
            tls = new { subject = observedCertificate.Subject, issuer = observedCertificate.Issuer,
                notBeforeUtc = observedCertificate.NotBefore.ToUniversalTime(), notAfterUtc = observedCertificate.NotAfter.ToUniversalTime(),
                sha256 = Convert.ToHexString(SHA256.HashData(observedCertificate.RawData)), observedAtUtc = DateTimeOffset.UtcNow, serverName = config.Host,
                remoteEndpoint = client.Client.RemoteEndPoint?.ToString(), protocol = ssl.SslProtocol.ToString(),
                cipherSuite = ssl.NegotiatedCipherSuite.ToString(), leafDerBase64 = leaf,
                builtChainDerBase64 = certificates, validationErrors = errors, chainStatuses = statuses,
                revocationChecked = false, chainDescription = "Chain built by the worker; may contain locally sourced certificates." };
        }
        var capturedAt = DateTimeOffset.UtcNow;
        var content = JsonSerializer.SerializeToUtf8Bytes(new
        { format = "priorstate-dns-tls-observation", formatVersion = 1, host = config.Host, resolver,
          resolverPort = config.ResolverPort, dns, tls }, JsonOptions);
        if (content.Length > policy.MaxPayloadBytes)
            throw new PluginException("The DNS/TLS observation exceeds Plugins:DnsTls:MaxPayloadBytes. Nothing was archived.");
        return new PluginPayload { Url = $"dns-tls://{config.Host}:{config.Port}", CapturedAtUtc = capturedAt,
            MediaType = "application/json", Content = content };
    }
}

