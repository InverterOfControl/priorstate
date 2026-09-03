using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PriorState.Domain.ValueObjects;

namespace PriorState.Data;

/// <summary>
/// Stores a hash as the same lowercase hex string that appears in the canonical form and in the
/// evidence package, so a value read straight out of the database with psql can be compared to a
/// value in a protocol by eye.
/// </summary>
public sealed class Sha256HashConverter : ValueConverter<Sha256Hash, string>
{
    public Sha256HashConverter()
        : base(hash => hash.Value, value => Sha256Hash.Parse(value))
    {
    }
}
