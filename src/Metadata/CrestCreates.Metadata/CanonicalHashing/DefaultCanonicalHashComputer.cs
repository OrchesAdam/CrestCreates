using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.CanonicalHashing.Generated;

namespace CrestCreates.Metadata.CanonicalHashing;

/// <summary>
/// Default implementation of <see cref="ICanonicalHashComputer"/>.
/// Uses SG-generated canonical JSON writer delegates for deterministic serialization.
/// No <c>JsonSerializer</c>, <c>JsonTypeInfo</c>, runtime <c>Type</c>, or reflection is involved.
///
/// The compute path:
///   1. Get projection result (carrying metadata + WriteCanonicalJson delegate)
///   2. Create ArrayBufferWriter + Utf8JsonWriter (deterministic options)
///   3. Invoke WriteCanonicalJson(writer) to produce canonical JSON bytes
///   4. SHA-256 over the bytes
///   5. Assemble CanonicalHash record with all metadata
/// </summary>
public sealed class DefaultCanonicalHashComputer : ICanonicalHashComputer
{
    private const string Algorithm = "SHA-256";
    public const string AlgorithmVersion = "sha256-canonical-json-v1";

    public CanonicalHash ComputeContractHash(IDescriptor descriptor, CanonicalHashScope scope)
    {
        var projection = CanonicalHashProjectionDispatcher.ToContractProjection(
            descriptor, scope, ContractVersions.DescriptorHash, AlgorithmVersion);
        return ComputeFromProjection(projection);
    }

    public CanonicalHash ComputeDefinitionHash(IDescriptor descriptor, CanonicalHashScope scope)
    {
        var projection = CanonicalHashProjectionDispatcher.ToDefinitionProjection(
            descriptor, scope, ContractVersions.DescriptorHash, AlgorithmVersion);
        return ComputeFromProjection(projection);
    }

    public CanonicalHash ComputeFromProjection(CanonicalHashProjectionResult projection)
    {
        ArgumentNullException.ThrowIfNull(projection);

        var bufferWriter = new ArrayBufferWriter<byte>(4096);
        using var jsonWriter = new Utf8JsonWriter(bufferWriter, new JsonWriterOptions
        {
            Indented = false,
            SkipValidation = true // canonical JSON is SG-generated — always valid
        });

        projection.WriteCanonicalJson(jsonWriter);
        jsonWriter.Flush();

        var hashBytes = SHA256.HashData(bufferWriter.WrittenSpan);
        var hashValue = Convert.ToHexString(hashBytes).ToLowerInvariant();

        return new CanonicalHash
        {
            Value = hashValue,
            Algorithm = Algorithm,
            AlgorithmVersion = projection.Metadata.AlgorithmVersion,
            ArtifactKind = projection.Metadata.ArtifactKind,
            DescriptorKind = projection.Metadata.DescriptorKind,
            Scope = projection.Metadata.Scope,
            Purpose = projection.Metadata.Purpose,
            ContractVersion = projection.Metadata.ContractVersion,
            CanonicalShapeVersion = projection.Metadata.CanonicalShapeVersion
        };
    }
}
