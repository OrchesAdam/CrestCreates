namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Container for stable, deterministic hashes computed from a descriptor's structural
/// content. Produced by <see cref="IDescriptorStableHashBuilder"/>.
///
/// <see cref="ContractHash"/> covers externally observable contract changes.
/// <see cref="DefinitionHash"/> covers any definition-level change (even if compatible).
/// <see cref="RuntimeHash"/> and <see cref="BindingHash"/> are reserved for future
/// separation of runtime binding state from descriptor definition state.
/// </summary>
public sealed record DescriptorStableHashes(
    string ContractHash,
    string DefinitionHash,
    string? RuntimeHash = null,
    string? BindingHash = null);
