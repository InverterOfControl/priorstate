using PriorState.Domain.ValueObjects;

namespace PriorState.Ledger.Timestamping;

/// <summary>Obtains an RFC-3161 token over a hash from an external authority.</summary>
public interface ITimestampAuthority
{
    /// <summary>
    /// Requests a timestamp over <paramref name="hash"/>. Throws
    /// <see cref="TimestampAuthorityException"/> if the authority refuses, is unreachable, or
    /// returns a token that does not commit to the hash that was sent.
    /// </summary>
    Task<TimestampResult> TimestampAsync(Sha256Hash hash, CancellationToken cancellationToken = default);
}

/// <summary>A token obtained from an authority, already checked against the requested hash.</summary>
public sealed record TimestampResult
{
    /// <summary>DER-encoded TimeStampToken, stored verbatim in the anchor.</summary>
    public required byte[] Token { get; init; }

    /// <summary>The time the authority asserts.</summary>
    public required DateTimeOffset GeneralizedTime { get; init; }

    public required string TsaUrl { get; init; }

    public required bool Qualified { get; init; }
}

public sealed class TimestampAuthorityException : Exception
{
    public TimestampAuthorityException(string message) : base(message)
    {
    }

    public TimestampAuthorityException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
