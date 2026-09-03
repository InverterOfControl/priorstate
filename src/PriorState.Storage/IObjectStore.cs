using PriorState.Domain.Entities;
using PriorState.Domain.ValueObjects;

namespace PriorState.Storage;

/// <summary>
/// Where WACZ archives and evidence packages live.
///
/// Deliberately has no delete method. Removing a snapshot is not a capability this system has, and
/// the absence is enforced at the narrowest point rather than by convention further up.
/// </summary>
public interface IObjectStore
{
    /// <summary>
    /// Stores an object and, where the backend genuinely supports it, applies WORM retention.
    /// The returned result says what the backend actually did — never what it claims to support.
    /// </summary>
    Task<ObjectWriteResult> PutAsync(
        string key,
        Stream content,
        string contentType,
        TimeSpan retention,
        CancellationToken cancellationToken = default);

    Task<Stream> GetAsync(string key, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// What the configured backend was observed to do, determined once at startup by
    /// <see cref="WormCapabilityProbe"/>.
    /// </summary>
    WormSupport WormCapability { get; }
}

public sealed record ObjectWriteResult
{
    public required string Key { get; init; }

    public required long SizeBytes { get; init; }

    /// <summary>SHA-256 computed while streaming, so the hash always matches the stored bytes.</summary>
    public required Sha256Hash Sha256 { get; init; }

    /// <summary>What actually happened for this object, not what the backend advertises.</summary>
    public required WormSupport Worm { get; init; }

    /// <summary>Set only when <see cref="Worm"/> is <see cref="WormSupport.Enforced"/>.</summary>
    public DateTimeOffset? RetainUntil { get; init; }
}

public sealed class ObjectStoreException : Exception
{
    public ObjectStoreException(string message) : base(message)
    {
    }

    public ObjectStoreException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
