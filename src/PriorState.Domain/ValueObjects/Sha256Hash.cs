namespace PriorState.Domain.ValueObjects;

/// <summary>
/// A SHA-256 digest, always rendered as lowercase hex. Rendering is fixed because these strings
/// appear in the canonical form that gets hashed and in the evidence package the opposing party
/// re-computes; a change in casing would break verification of existing packages.
/// </summary>
public readonly record struct Sha256Hash
{
    public const int ByteLength = 32;
    public const int HexLength = 64;

    /// <summary>The hash of an empty chain: the predecessor of the very first ledger entry.</summary>
    public static Sha256Hash Genesis { get; } = new(new string('0', HexLength));

    private Sha256Hash(string hex) => Value = hex;

    public string Value { get; }

    public static Sha256Hash FromBytes(ReadOnlySpan<byte> digest)
    {
        if (digest.Length != ByteLength)
        {
            throw new ArgumentException($"A SHA-256 digest is {ByteLength} bytes, got {digest.Length}.", nameof(digest));
        }

        return new Sha256Hash(Convert.ToHexStringLower(digest));
    }

    public static Sha256Hash Parse(string hex)
    {
        if (!TryParse(hex, out var hash))
        {
            throw new FormatException($"Not a lowercase hex SHA-256 digest: '{hex}'.");
        }

        return hash;
    }

    public static bool TryParse(string? hex, out Sha256Hash hash)
    {
        hash = default;
        if (hex is not { Length: HexLength })
        {
            return false;
        }

        foreach (var c in hex)
        {
            if (c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
            {
                return false;
            }
        }

        hash = new Sha256Hash(hex);
        return true;
    }

    public byte[] ToBytes() => Convert.FromHexString(Value);

    public override string ToString() => Value;
}
