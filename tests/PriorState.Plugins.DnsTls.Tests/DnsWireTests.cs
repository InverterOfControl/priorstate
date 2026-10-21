using Microsoft.Extensions.Options;
using PriorState.Plugins.Abstractions;
using PriorState.Plugins.DnsTls;

namespace PriorState.Plugins.DnsTls.Tests;

public sealed class DnsWireTests
{
    [Theory]
    [InlineData(28, "AAAAAAAAAAAAAAAAAAAAAQ==", "::1")]
    [InlineData(15, "AAoCbXgHZXhhbXBsZQNjb20A", "10 mx.example.com")]
    [InlineData(16, "A2ZvbwQjYmFy", "foo | #bar")]
    [InlineData(2, "Am5zB2V4YW1wbGUDY29tAA==", "ns.example.com")]
    [InlineData(257, "AAVpc3N1ZWNhLmV4YW1wbGU=", "0 issue ca.example")]
    public void DecodesRecordTypes(int type, string encoded, string expected)
    {
        var query = DnsWire.CreateQuery("example.com", (ushort)type);
        var data = Convert.FromBase64String(encoded);
        var recordHeader = new byte[] { 192, 12, (byte)(type >> 8), (byte)type, 0, 1, 0, 0, 0, 60, (byte)(data.Length >> 8), (byte)data.Length };
        var response = query.Concat(recordHeader).Concat(data).ToArray();
        response[2] = 129; response[7] = 1;
        Assert.Equal(expected, Assert.Single(DnsWire.ParseResponse(query, response).Records).Value);
    }

    [Fact]
    public void PreservesRecordBytesAndTtl()
    {
        var query = DnsWire.CreateQuery("example.com", 1);
        var response = query.Concat(new byte[] { 192, 12, 0, 1, 0, 1, 0, 0, 0, 60, 0, 4, 192, 0, 2, 1 }).ToArray();
        response[2] = 129; response[3] = 128; response[7] = 1;
        var record = Assert.Single(DnsWire.ParseResponse(query, response).Records);
        Assert.Equal("example.com", record.Name);
        Assert.Equal(60u, record.Ttl);
        Assert.Equal("192.0.2.1", record.Value);
        Assert.Equal(new byte[] { 192, 0, 2, 1 }, Convert.FromBase64String(record.DataBase64));
    }

    [Fact]
    public void RejectsCompressionLoop()
    {
        var query = DnsWire.CreateQuery("example.com", 1);
        var response = query.Concat(new byte[] { 192, (byte)query.Length, 0, 1, 0, 1, 0, 0, 0, 60, 0, 4, 192, 0, 2, 1 }).ToArray();
        response[2] = 129; response[7] = 1;
        Assert.Throws<PluginException>(() => DnsWire.ParseResponse(query, response));
    }

    [Fact]
    public void ArchivesNxDomainButRejectsResolverFailure()
    {
        var query = DnsWire.CreateQuery("example.com", 1);
        var response = (byte[])query.Clone(); response[2] = 129; response[3] = 3;
        Assert.Equal(3, DnsWire.ParseResponse(query, response).ResponseCode);
        response[3] = 2;
        Assert.Throws<PluginException>(() => DnsWire.ParseResponse(query, response));
    }

    [Fact]
    public void RejectsMismatchedTransactionAndTruncation()
    {
        var query = DnsWire.CreateQuery("example.com", 1);
        var response = (byte[])query.Clone(); response[2] = 131;
        Assert.Throws<PluginException>(() => DnsWire.ParseResponse(query, response));
        response[2] = 129; response[0] ^= 1;
        Assert.Throws<PluginException>(() => DnsWire.ParseResponse(query, response));
    }

    [Theory]
    [InlineData("{\"host\":\"example.com\",\"port\":0}")]
    [InlineData("{\"host\":\"example.com\",\"resolver\":\"https://dns.example\"}")]
    [InlineData("{\"host\":\"example.com\",\"captureDns\":false,\"captureTls\":false}")]
    [InlineData("{\"host\":\"user:password@example.com\"}")]
    public void RejectsInvalidConfiguration(string json)
    {
        var plugin = new DnsTlsCapturePlugin(Options.Create(new DnsTlsOptions()));
        Assert.Throws<PluginException>(() => plugin.ValidateConfiguration(json, "test", null));
    }
}

