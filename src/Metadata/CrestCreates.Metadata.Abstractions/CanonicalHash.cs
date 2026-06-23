namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Unified canonical hash result carrying metadata about how the hash was computed.
/// Replaces bare string hash values with a structured, versioned result.
/// </summary>
public sealed record CanonicalHash
{
    /// <summary>
    /// The hash digest value as lowercase hex string.
    /// </summary>
    public required string Value { get; init; }

    /// <summary>
    /// The hash algorithm used (e.g., "SHA-256").
    /// </summary>
    public required string Algorithm { get; init; }

    /// <summary>
    /// Version of the hash algorithm pipeline (e.g., "sha256-canonical-json-v1").
    /// </summary>
    public required string AlgorithmVersion { get; init; }

    /// <summary>
    /// What kind of artifact was hashed (e.g., "Descriptor", "ReviewResult", "Package", "Report").
    /// </summary>
    public required string ArtifactKind { get; init; }

    /// <summary>
    /// The descriptor kind, if ArtifactKind is "Descriptor". Null for non-descriptor artifacts.
    /// </summary>
    public string? DescriptorKind { get; init; }

    /// <summary>
    /// Which visibility boundary was used for hash computation (e.g., "InternalFull", "TenantVisible").
    /// Scope is domain-separation metadata only — it does not authorize or filter input.
    /// </summary>
    public required string Scope { get; init; }

    /// <summary>
    /// Why this hash was computed (e.g., "Contract", "Definition", "SourceBinding", "Integrity", "AuditEvidence").
    /// Governs timestamp inclusion rules and semantic guarantees.
    /// </summary>
    public required string Purpose { get; init; }

    /// <summary>
    /// Version of the hash contract (e.g., "canonical-hash-v1").
    /// </summary>
    public required string ContractVersion { get; init; }

    /// <summary>
    /// Version of the canonical shape (field set + ordering), e.g., "schema-contract-hash-v1".
    /// String, not int — the shape version is a contract identifier, not a number.
    /// </summary>
    public required string CanonicalShapeVersion { get; init; }
}
