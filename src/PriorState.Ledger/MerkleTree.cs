using System.Security.Cryptography;
using PriorState.Domain.ValueObjects;

namespace PriorState.Ledger;

/// <summary>
/// Binary Merkle tree over a day of entry hashes. One RFC-3161 token per day covers every entry
/// in that day, and any single entry can later be proven to belong to the timestamped root with a
/// short audit path rather than the whole day.
///
/// Domain separation follows RFC 6962: leaves are hashed with a 0x00 prefix and internal nodes
/// with 0x01, so a leaf value can never be passed off as an internal node. An odd node at a level
/// is promoted unchanged, which is the common convention and the one verify.sh implements.
/// </summary>
public static class MerkleTree
{
    private const byte LeafPrefix = 0x00;
    private const byte NodePrefix = 0x01;

    /// <summary>
    /// Computes the root over the given entry hashes, in the order supplied — which must be
    /// ascending chain sequence. Order is part of the commitment.
    /// </summary>
    public static Sha256Hash ComputeRoot(IReadOnlyList<Sha256Hash> entryHashes)
    {
        ArgumentNullException.ThrowIfNull(entryHashes);
        if (entryHashes.Count == 0)
        {
            throw new ArgumentException("Cannot compute a Merkle root over zero entries.", nameof(entryHashes));
        }

        var level = new List<byte[]>(entryHashes.Count);
        foreach (var hash in entryHashes)
        {
            level.Add(HashLeaf(hash));
        }

        while (level.Count > 1)
        {
            var next = new List<byte[]>((level.Count + 1) / 2);
            for (var i = 0; i < level.Count; i += 2)
            {
                if (i + 1 < level.Count)
                {
                    next.Add(HashNode(level[i], level[i + 1]));
                }
                else
                {
                    // Odd one out: promoted unchanged to the next level.
                    next.Add(level[i]);
                }
            }

            level = next;
        }

        return Sha256Hash.FromBytes(level[0]);
    }

    /// <summary>
    /// The audit path proving <paramref name="index"/> is part of the root: the sibling hashes
    /// from leaf to root, bottom-up. Included in an evidence package so a single snapshot can be
    /// verified against the day's timestamp without shipping every other snapshot from that day.
    /// </summary>
    public static IReadOnlyList<Sha256Hash> ComputeAuditPath(IReadOnlyList<Sha256Hash> entryHashes, int index)
    {
        ArgumentNullException.ThrowIfNull(entryHashes);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, entryHashes.Count);

        var level = new List<byte[]>(entryHashes.Count);
        foreach (var hash in entryHashes)
        {
            level.Add(HashLeaf(hash));
        }

        var path = new List<Sha256Hash>();
        var position = index;

        while (level.Count > 1)
        {
            var sibling = (position % 2 == 0) ? position + 1 : position - 1;
            if (sibling < level.Count)
            {
                path.Add(Sha256Hash.FromBytes(level[sibling]));
            }

            var next = new List<byte[]>((level.Count + 1) / 2);
            for (var i = 0; i < level.Count; i += 2)
            {
                next.Add(i + 1 < level.Count ? HashNode(level[i], level[i + 1]) : level[i]);
            }

            level = next;
            position /= 2;
        }

        return path;
    }

    private static byte[] HashLeaf(Sha256Hash entryHash)
    {
        Span<byte> buffer = stackalloc byte[1 + Sha256Hash.ByteLength];
        buffer[0] = LeafPrefix;
        entryHash.ToBytes().CopyTo(buffer[1..]);
        return SHA256.HashData(buffer);
    }

    private static byte[] HashNode(byte[] left, byte[] right)
    {
        Span<byte> buffer = stackalloc byte[1 + (Sha256Hash.ByteLength * 2)];
        buffer[0] = NodePrefix;
        left.CopyTo(buffer[1..]);
        right.CopyTo(buffer[(1 + Sha256Hash.ByteLength)..]);
        return SHA256.HashData(buffer);
    }
}
