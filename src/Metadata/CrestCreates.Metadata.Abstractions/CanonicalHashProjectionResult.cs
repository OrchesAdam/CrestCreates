using System.Text.Json;

namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// A pre-built projection result ready for canonical hashing.
/// Carries metadata and a canonical JSON writer delegate for constructing the <see cref="CanonicalHash"/> result.
/// </summary>
/// <remarks>
/// Use <see cref="Create"/> to construct — prevents metadata/writer mismatch.
/// The runtime must invoke <see cref="WriteCanonicalJson"/> via <see cref="Utf8JsonWriter"/>
/// to produce deterministic canonical JSON bytes, then apply SHA-256.
/// No <c>JsonSerializer</c>, <c>JsonTypeInfo</c>, runtime <c>Type</c>, or reflection is involved.
/// </remarks>
public sealed record CanonicalHashProjectionResult(CanonicalHashMetadata Metadata, Action<Utf8JsonWriter> WriteCanonicalJson)
{
    /// <summary>
    /// Factory method to create a <see cref="CanonicalHashProjectionResult"/>.
    /// </summary>
    /// <param name="metadata">Metadata for domain separation and CanonicalHash construction.</param>
    /// <param name="writeCanonicalJson">
    /// SG-generated delegate that writes canonical JSON to a Utf8JsonWriter.
    /// The delegate must write the complete envelope object (metadata + payload).
    /// </param>
    /// <returns>A projection result ready for <see cref="ICanonicalHashComputer.ComputeFromProjection"/>.</returns>
    public static CanonicalHashProjectionResult Create(
        CanonicalHashMetadata metadata,
        Action<Utf8JsonWriter> writeCanonicalJson)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(writeCanonicalJson);
        return new CanonicalHashProjectionResult(metadata, writeCanonicalJson);
    }
}

/// <summary>
/// Metadata for canonical hash computation — domain separation and CanonicalHash construction.
/// Passed to <see cref="CanonicalHashProjectionResult.Create"/> alongside the SG-generated writer delegate.
/// </summary>
public sealed record CanonicalHashMetadata
{
    /// <summary>
    /// What kind of artifact is being hashed (e.g., "Descriptor", "ReviewResult", "Package").
    /// </summary>
    public required string ArtifactKind { get; init; }

    /// <summary>
    /// The descriptor kind, if ArtifactKind is "Descriptor". Null for non-descriptor artifacts.
    /// </summary>
    public string? DescriptorKind { get; init; }

    /// <summary>
    /// Why this hash is being computed (e.g., "Contract", "Definition").
    /// </summary>
    public required string Purpose { get; init; }

    /// <summary>
    /// Which visibility boundary was used for hash computation.
    /// </summary>
    public required string Scope { get; init; }

    /// <summary>
    /// The hash algorithm pipeline version (e.g., "sha256-canonical-json-v1").
    /// </summary>
    public required string AlgorithmVersion { get; init; }

    /// <summary>
    /// The hash contract version (e.g., "canonical-hash-v1").
    /// </summary>
    public required string ContractVersion { get; init; }

    /// <summary>
    /// The canonical shape version string (e.g., "schema-contract-hash-v1").
    /// </summary>
    public required string CanonicalShapeVersion { get; init; }
}
