namespace PriorState.Storage;

/// <summary>S3-compatible object storage configuration. Bound from "Storage".</summary>
public sealed class ObjectStoreOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// S3 endpoint. Defaults to the Garage container in the bundled compose file. Garage is the
    /// default because it is light, healthy and easy to run — not because it provides WORM. It
    /// does not; see <see cref="WormCapabilityProbe"/> and the storage page in the docs.
    /// </summary>
    public string ServiceUrl { get; set; } = "http://garage:3900";

    public string Region { get; set; } = "garage";

    public required string AccessKey { get; set; }

    public required string SecretKey { get; set; }

    public string Bucket { get; set; } = "priorstate";

    /// <summary>
    /// Path-style addressing. Required by Garage, MinIO and most self-hosted implementations;
    /// hosted providers generally accept it too.
    /// </summary>
    public bool ForcePathStyle { get; set; } = true;

    /// <summary>
    /// Attempt to apply S3 Object Lock retention to stored objects. Left on by default: where the
    /// backend enforces it this is real defence in depth, and where it does not the probe records
    /// that honestly rather than silently pretending.
    /// </summary>
    public bool UseObjectLock { get; set; } = true;

    /// <summary>
    /// Run the write-then-delete enforcement probe at startup. Turning this off downgrades a
    /// backend that would otherwise report Enforced to ApiPresentUnverified, because an
    /// unverified claim of immutability is not one this project is willing to print on a protocol.
    /// </summary>
    public bool ProbeWormEnforcement { get; set; } = true;
}
