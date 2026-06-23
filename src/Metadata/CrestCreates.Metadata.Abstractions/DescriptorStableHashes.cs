namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Canonical hashes for a single descriptor.
/// ContractHash changes when the externally visible contract changes.
/// DefinitionHash changes on any definition-level change (broader than contract).
/// RuntimeHash and BindingHash are reserved for future use.
/// </summary>
public sealed record DescriptorStableHashes
{
    /// <summary>
    /// Hash of externally observable contract fields.
    /// Changes when the descriptor's invocation, execution, binding, or I/O structure changes.
    /// </summary>
    public required CanonicalHash ContractHash { get; init; }

    /// <summary>
    /// Hash of all definition fields (contract + definition-only).
    /// Changes when any field changes, including display metadata.
    /// </summary>
    public required CanonicalHash DefinitionHash { get; init; }

    /// <summary>
    /// Reserved for future runtime binding state separation.
    /// </summary>
    public CanonicalHash? RuntimeHash { get; init; }

    /// <summary>
    /// Reserved for future descriptor definition binding separation.
    /// </summary>
    public CanonicalHash? BindingHash { get; init; }
}
