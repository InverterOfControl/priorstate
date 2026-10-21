using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using PriorState.Plugins.Abstractions;

namespace PriorState.Plugins.DnsTls;

public sealed record DnsRecord(string Section, string Name, ushort Type, ushort Class, uint Ttl,
    string DataBase64, string Value);
public sealed record DnsResponse(int ResponseCode, IReadOnlyList<DnsRecord> Records);

public static class DnsWire
{
    public static byte[] CreateQuery(string host, ushort type)
    {
        using var buffer = new MemoryStream();
        var header = new byte[12];
        RandomNumberGenerator.Fill(header.AsSpan(0, 2));
        header[2] = 1; header[5] = 1;
        buffer.Write(header);
        foreach (var label in host.TrimEnd('.').Split('.'))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            if (bytes.Length is < 1 or > 63 || label.Any(c => c > 127))
                throw new PluginException("DNS names must use ASCII/punycode labels of 1–63 bytes.");
            buffer.WriteByte((byte)bytes.Length); buffer.Write(bytes);
        }
        buffer.WriteByte(0);
        var tail = new byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(tail, type); tail[3] = 1;
        buffer.Write(tail);
        return buffer.ToArray();
    }

    public static DnsResponse ParseResponse(byte[] query, byte[] message)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(message);
        if (message.Length < 12 || query.Length < 12 || message[0] != query[0] || message[1] != query[1]
            || (message[2] & 128) == 0 || (message[2] & 2) != 0)
            throw new PluginException("Invalid or truncated DNS response.");
        var offset = 12;
        var questions = UInt16(message, 4);
        if (questions != 1) throw new PluginException("Unexpected DNS question count.");
        _ = Name(message, ref offset);
        Require(message, offset, 4); offset += 4;
        if (!query.AsSpan(12).SequenceEqual(message.AsSpan(12, offset - 12)))
            throw new PluginException("DNS response does not match the requested question.");
        var records = new List<DnsRecord>();
        string[] sections = ["answer", "authority", "additional"];
        for (var section = 0; section < 3; section++)
        {
            var count = UInt16(message, 6 + section * 2);
            for (var i = 0; i < count; i++)
            {
                var name = Name(message, ref offset);
                Require(message, offset, 10);
                var type = UInt16(message, offset); var recordClass = UInt16(message, offset + 2);
                var ttl = BinaryPrimitives.ReadUInt32BigEndian(message.AsSpan(offset + 4, 4));
                var length = UInt16(message, offset + 8); offset += 10;
                Require(message, offset, length);
                var start = offset;
                var value = Render(message, type, start, length);
                records.Add(new(sections[section], name, type, recordClass, ttl,
                    Convert.ToBase64String(message, start, length), value));
                offset += length;
            }
        }
        if (offset != message.Length) throw new PluginException("DNS response contains unaccounted bytes.");
        var rcode = message[3] & 15;
        if (rcode is not (0 or 3)) throw new PluginException($"DNS resolver returned error code {rcode}.");
        return new(rcode, records);
    }

    private static string Render(byte[] message, ushort type, int start, int length)
    {
        var position = start;
        var end = start + length;
        string result;
        switch (type)
        {
            case 1 when length == 4:
            case 28 when length == 16:
                return new System.Net.IPAddress(message.AsSpan(start, length)).ToString();
            case 2: case 5: case 12:
                result = Name(message, ref position); break;
            case 15:
                Require(message, position, 2);
                var preference = UInt16(message, position); position += 2;
                result = $"{preference} {Name(message, ref position)}"; break;
            case 6:
                var primary = Name(message, ref position); var mailbox = Name(message, ref position);
                Require(message, position, 20);
                var numbers = new uint[5];
                for (var i = 0; i < 5; i++) numbers[i] = BinaryPrimitives.ReadUInt32BigEndian(message.AsSpan(position + i * 4, 4));
                position += 20; result = $"{primary} {mailbox} {string.Join(' ', numbers)}"; break;
            case 16:
                var texts = new List<string>();
                while (position < end)
                {
                    var count = message[position++];
                    if (position + count > end) throw new PluginException("Malformed DNS TXT record.");
                    texts.Add(Encoding.UTF8.GetString(message, position, count)); position += count;
                }
                result = string.Join(" | ", texts); break;
            case 257:
                if (length < 2 || message[start + 1] > length - 2) throw new PluginException("Malformed DNS CAA record.");
                var tagLength = message[start + 1];
                result = $"{message[start]} {Encoding.ASCII.GetString(message, start + 2, tagLength)} {Encoding.UTF8.GetString(message, start + 2 + tagLength, length - 2 - tagLength)}";
                position = end; break;
            default: return Convert.ToBase64String(message, start, length);
        }
        if (position != end) throw new PluginException("Malformed DNS record length.");
        return result;
    }

    private static string Name(byte[] message, ref int offset)
    {
        var position = offset; var jumped = false;
        var labels = new List<string>();
        for (var hops = 0; hops < 128; hops++)
        {
            Require(message, position, 1);
            var count = message[position++];
            if (count == 0) { if (!jumped) offset = position; return string.Join('.', labels); }
            if ((count & 192) == 192)
            {
                Require(message, position, 1);
                var pointer = ((count & 63) << 8) | message[position++];
                if (!jumped) offset = position;
                jumped = true; position = pointer; continue;
            }
            if (count > 63) throw new PluginException("Invalid DNS label.");
            Require(message, position, count);
            labels.Add(Encoding.ASCII.GetString(message, position, count)); position += count;
            if (!jumped) offset = position;
            if (labels.Sum(l => l.Length + 1) > 254) throw new PluginException("DNS name is too long.");
        }
        throw new PluginException("DNS compression loop or excessive indirection.");
    }

    private static ushort UInt16(byte[] message, int offset)
    { Require(message, offset, 2); return BinaryPrimitives.ReadUInt16BigEndian(message.AsSpan(offset, 2)); }
    private static void Require(byte[] message, int offset, int count)
    { if (offset < 0 || count < 0 || offset > message.Length - count) throw new PluginException("Malformed DNS message."); }
}
