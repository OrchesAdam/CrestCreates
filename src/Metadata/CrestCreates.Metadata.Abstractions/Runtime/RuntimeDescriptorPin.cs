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

        if (string.IsNullOrWhiteSpace(ContractHash.Value))
            throw new ArgumentException("Contract hash digest is required.", nameof(ContractHash));
        if (string.IsNullOrWhiteSpace(DefinitionHash.Value))
            throw new ArgumentException("Definition hash digest is required.", nameof(DefinitionHash));
        if (!string.Equals(ContractHash.Purpose, "Contract", StringComparison.Ordinal))
            throw new ArgumentException("Contract hash purpose must be Contract.", nameof(ContractHash));
        if (!string.Equals(DefinitionHash.Purpose, "Definition", StringComparison.Ordinal))
            throw new ArgumentException("Definition hash purpose must be Definition.", nameof(DefinitionHash));
        if (!string.Equals(ContractHash.Scope, "InternalFull", StringComparison.Ordinal)
            || !string.Equals(DefinitionHash.Scope, "InternalFull", StringComparison.Ordinal))
            throw new ArgumentException("Executable descriptor pins require InternalFull hash scope.", nameof(ContractHash));
    }
}
