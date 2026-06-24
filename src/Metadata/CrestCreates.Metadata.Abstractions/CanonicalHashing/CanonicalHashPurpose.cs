namespace CrestCreates.Metadata.Abstractions.CanonicalHashing;

/// <summary>
/// Why a canonical hash is being computed.
/// Governs timestamp inclusion rules and semantic guarantees.
/// </summary>
public enum CanonicalHashPurpose
{
    /// <summary>
    /// Descriptor ContractHash — externally observable contract fields only.
    /// Must not include generated timestamps.
    /// </summary>
    Contract = 1,

    /// <summary>
    /// Descriptor DefinitionHash — contract + definition-only fields.
    /// Must not include generated timestamps.
    /// </summary>
    Definition = 2,

    /// <summary>
    /// "Is this the same source artifact?" — SourceReviewHash, ReportId.
    /// Must not include generated timestamps.
    /// </summary>
    SourceBinding = 3,

    /// <summary>
    /// "Has this package been tampered with?" — EnvelopeHash, ContentHash.
    /// May include creation metadata only when that metadata is part of the artifact envelope contract.
    /// </summary>
    Integrity = 4,

    /// <summary>
    /// "What was the exact state at time T?" — for audit trail integrity.
    /// May include timestamps when audit trail integrity requires them.
    /// </summary>
    AuditEvidence = 5
}
