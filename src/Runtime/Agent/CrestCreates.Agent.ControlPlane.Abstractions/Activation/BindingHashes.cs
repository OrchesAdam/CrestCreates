using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.ControlPlane.Abstractions.Activation;

/// <summary>
/// Immutable hash evidence bound to an activation request.
/// All hashes use the full CanonicalHash model (not bare strings)
/// per the evidence contract requirement.
/// </summary>
public sealed record BindingHashes
{
    public required CanonicalHash SourceReviewHash { get; init; }
    public required CanonicalHash ManifestHash { get; init; }
    public required CanonicalHash EvidenceHash { get; init; }
    public required CanonicalHash EnvelopeHash { get; init; }
    public required CanonicalHash ContractHash { get; init; }
    public required CanonicalHash DefinitionHash { get; init; }
}
