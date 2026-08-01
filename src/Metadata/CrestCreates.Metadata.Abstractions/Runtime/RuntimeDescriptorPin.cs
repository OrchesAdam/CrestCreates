using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Metadata.Abstractions.Runtime;

/// <summary>
/// Immutable executable Descriptor identity. The durable snapshot is evidence;
/// this pin is resolved against the active, versioned Registry at recovery time.
/// </summary>
public sealed record RuntimeDescriptorPin
{
    public required DescriptorRef Ref { get; init; }

    public required CanonicalHash ContractHash { get; init; }

    public required CanonicalHash DefinitionHash { get; init; }

    public string? SnapshotId { get; init; }

    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(Ref.Namespace))
            throw new ArgumentException("Descriptor namespace is required.", nameof(Ref));
        if (string.IsNullOrWhiteSpace(Ref.Id))
            throw new ArgumentException("Descriptor ID is required.", nameof(Ref));
        if (Ref.Version is not > 0)
            throw new ArgumentException("Descriptor pin requires an exact positive version.", nameof(Ref));

        EnsureHash(ContractHash, "Contract", nameof(ContractHash));
        EnsureHash(DefinitionHash, "Definition", nameof(DefinitionHash));
    }

    private static void EnsureHash(CanonicalHash hash, string purpose, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(hash);
        if (string.IsNullOrWhiteSpace(hash.Value)
            || string.IsNullOrWhiteSpace(hash.Algorithm)
            || string.IsNullOrWhiteSpace(hash.AlgorithmVersion)
            || string.IsNullOrWhiteSpace(hash.ArtifactKind)
            || string.IsNullOrWhiteSpace(hash.ContractVersion)
            || string.IsNullOrWhiteSpace(hash.CanonicalShapeVersion))
        {
            throw new ArgumentException("Descriptor hash profile is incomplete.", parameterName);
        }
        if (string.Equals(hash.Value, "unresolved", StringComparison.OrdinalIgnoreCase)
            || string.Equals(hash.Algorithm, "unresolved", StringComparison.OrdinalIgnoreCase)
            || string.Equals(hash.AlgorithmVersion, "unresolved", StringComparison.OrdinalIgnoreCase)
            || string.Equals(hash.ContractVersion, "unresolved", StringComparison.OrdinalIgnoreCase)
            || string.Equals(hash.CanonicalShapeVersion, "unresolved", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Placeholder descriptor hashes cannot be persisted or executed.", parameterName);
        }
        if (!string.Equals(hash.Purpose, purpose, StringComparison.Ordinal))
            throw new ArgumentException($"Descriptor hash purpose must be {purpose}.", parameterName);
        if (!string.Equals(hash.Scope, "InternalFull", StringComparison.Ordinal))
            throw new ArgumentException("Executable descriptor pins require InternalFull hash scope.", parameterName);
    }
}
