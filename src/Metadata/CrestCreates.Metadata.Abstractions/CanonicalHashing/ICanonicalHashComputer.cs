namespace CrestCreates.Metadata.Abstractions.CanonicalHashing;

/// <summary>
/// Computes canonical hashes for descriptors and other artifacts.
/// The computer is scope-agnostic — it hashes what it's given.
/// Visibility filtering is the responsibility of artifact-specific projections, not the hash computer.
///
/// AOT hard rule: ComputeFromProjection uses only the WriteCanonicalJson delegate
/// from the projection — no JsonSerializer, no JsonTypeInfo, no runtime Type, no reflection.
/// </summary>
public interface ICanonicalHashComputer
{
    /// <summary>
    /// Compute ContractHash for a descriptor.
    /// Uses SG-generated ContractHashPayload projection + canonical writer.
    /// </summary>
    CanonicalHash ComputeContractHash(IDescriptor descriptor, CanonicalHashScope scope);

    /// <summary>
    /// Compute DefinitionHash for a descriptor.
    /// Uses SG-generated DefinitionHashPayload projection + canonical writer.
    /// </summary>
    CanonicalHash ComputeDefinitionHash(IDescriptor descriptor, CanonicalHashScope scope);

    /// <summary>
    /// Compute a canonical hash from a pre-built projection result.
    /// For hand-written artifact projectors (ReviewResult, Package, ReportId, etc.).
    /// Uses the WriteCanonicalJson delegate — no JsonSerializer, no JsonTypeInfo, no reflection.
    /// </summary>
    CanonicalHash ComputeFromProjection(CanonicalHashProjectionResult projection);
}
