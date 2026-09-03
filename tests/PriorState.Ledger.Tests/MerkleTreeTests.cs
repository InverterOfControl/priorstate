using System.Security.Cryptography;
using PriorState.Domain.ValueObjects;
using PriorState.Ledger;

namespace PriorState.Ledger.Tests;

public sealed class MerkleTreeTests
{
    private static Sha256Hash Leaf(byte seed) =>
        Sha256Hash.FromBytes(SHA256.HashData([seed]));

    private static List<Sha256Hash> Leaves(int count) =>
        [.. Enumerable.Range(0, count).Select(i => Leaf((byte)i))];

    [Fact]
    public void ComputeRoot_OfASingleEntryIsTheHashedLeafNotTheEntryItself()
    {
        var leaf = Leaf(1);

        var root = MerkleTree.ComputeRoot([leaf]);

        // Domain separation: a bare entry hash must never be usable as a root.
        Assert.NotEqual(leaf, root);
    }

    [Fact]
    public void ComputeRoot_IsDeterministic()
    {
        var leaves = Leaves(9);

        Assert.Equal(MerkleTree.ComputeRoot(leaves), MerkleTree.ComputeRoot(leaves));
    }

    [Fact]
    public void ComputeRoot_DependsOnOrder()
    {
        var forwards = Leaves(8);
        var backwards = Enumerable.Reverse(forwards).ToList();

        Assert.NotEqual(MerkleTree.ComputeRoot(forwards), MerkleTree.ComputeRoot(backwards));
    }

    [Fact]
    public void ComputeRoot_ChangesWhenAnyLeafChanges()
    {
        var leaves = Leaves(16);
        var baseline = MerkleTree.ComputeRoot(leaves);

        leaves[11] = Leaf(200);

        Assert.NotEqual(baseline, MerkleTree.ComputeRoot(leaves));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(100)]
    public void ComputeRoot_HandlesOddAndEvenLevels(int count)
    {
        Assert.NotEqual(default, MerkleTree.ComputeRoot(Leaves(count)));
    }

    [Fact]
    public void ComputeRoot_RejectsAnEmptyDay()
    {
        Assert.Throws<ArgumentException>(() => MerkleTree.ComputeRoot([]));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(16)]
    [InlineData(37)]
    public void ComputeAuditPath_ProvesEveryLeafBelongsToTheRoot(int count)
    {
        var leaves = Leaves(count);
        var root = MerkleTree.ComputeRoot(leaves);

        for (var index = 0; index < count; index++)
        {
            var path = MerkleTree.ComputeAuditPath(leaves, index);

            Assert.Equal(root, ReplayAuditPath(leaves[index], index, count, path));
        }
    }

    [Fact]
    public void ComputeAuditPath_RejectsAnIndexOutsideTheDay()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MerkleTree.ComputeAuditPath(Leaves(4), 4));
    }

    /// <summary>
    /// Independent re-implementation of path replay — deliberately not calling into MerkleTree,
    /// so a bug in the tree cannot be masked by the same bug in the verifier.
    /// </summary>
    private static Sha256Hash ReplayAuditPath(
        Sha256Hash leaf, int index, int leafCount, IReadOnlyList<Sha256Hash> path)
    {
        Span<byte> leafBuffer = stackalloc byte[1 + Sha256Hash.ByteLength];
        leafBuffer[0] = 0x00;
        leaf.ToBytes().CopyTo(leafBuffer[1..]);
        var current = SHA256.HashData(leafBuffer);

        var position = index;
        var width = leafCount;
        var pathIndex = 0;
        Span<byte> nodeBuffer = stackalloc byte[1 + (Sha256Hash.ByteLength * 2)];
        nodeBuffer[0] = 0x01;

        while (width > 1)
        {
            var hasSibling = (position % 2 != 0) || position + 1 < width;
            if (hasSibling)
            {
                var sibling = path[pathIndex++].ToBytes();
                var left = (position % 2 == 0) ? current : sibling;
                var right = (position % 2 == 0) ? sibling : current;

                left.CopyTo(nodeBuffer[1..]);
                right.CopyTo(nodeBuffer[(1 + Sha256Hash.ByteLength)..]);
                current = SHA256.HashData(nodeBuffer);
            }

            position /= 2;
            width = (width + 1) / 2;
        }

        Assert.Equal(path.Count, pathIndex);
        return Sha256Hash.FromBytes(current);
    }
}
