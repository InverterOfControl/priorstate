using PriorState.Storage;

namespace PriorState.Storage.Tests;

/// <summary>
/// The defaults here are a design position, not a convenience, so they are pinned by test.
///
/// PriorState ships Garage as the bundled object store even though Garage does not implement S3
/// Object Lock. That is deliberate: the tamper-evidence rests on the hash chain and the external
/// RFC-3161 timestamps, which survive the bucket being wiped entirely. Storage immutability is
/// defence in depth, probed at startup and reported per snapshot rather than assumed.
/// </summary>
public sealed class ObjectStoreOptionsTests
{
    [Fact]
    public void Defaults_PointAtTheBundledGarageContainer()
    {
        var options = new ObjectStoreOptions { AccessKey = "k", SecretKey = "s" };

        Assert.Equal("http://garage:3900", options.ServiceUrl);
        Assert.Equal("priorstate", options.Bucket);
    }

    [Fact]
    public void Defaults_UsePathStyleAddressing()
    {
        // Required by Garage, MinIO, Ceph RGW and most self-hosted implementations. Getting this
        // wrong produces DNS errors that look like a network fault rather than a config mistake.
        var options = new ObjectStoreOptions { AccessKey = "k", SecretKey = "s" };

        Assert.True(options.ForcePathStyle);
    }

    [Fact]
    public void Defaults_AttemptObjectLockAndVerifyIt()
    {
        var options = new ObjectStoreOptions { AccessKey = "k", SecretKey = "s" };

        // Attempting it costs nothing on a backend that lacks it, and on one that has it the
        // probe is what turns an advertised capability into an observed one.
        Assert.True(options.UseObjectLock);
        Assert.True(options.ProbeWormEnforcement);
    }
}
