using System.Globalization;
using System.Text.Json;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;

namespace CrestCreates.Metadata.DescriptorPackage.CanonicalHashing;

/// <summary>
/// Canonical JSON writer for DescriptorPackageEvidenceEnvelope.
/// Property order: packageId, packageVersion, createdAt, createdBy, source,
/// packageManifestHash, packageEvidenceHash.
/// Hash properties are written as full objects, not just .Value strings.
/// </summary>
public static class DescriptorPackageEvidenceEnvelopeCanonicalHashWriter
{
    public static void WritePayload(Utf8JsonWriter writer, DescriptorPackageEvidenceEnvelope envelope)
    {
        writer.WriteStartObject();
        writer.WriteString("packageId", envelope.PackageId);
        writer.WriteString("packageVersion", envelope.PackageVersion);
        writer.WriteString("createdAt", envelope.CreatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        writer.WriteString("createdBy", envelope.CreatedBy);
        writer.WriteString("source", envelope.Source);
        writer.WritePropertyName("packageManifestHash");
        WriteCanonicalHash(writer, envelope.PackageManifestHash);
        writer.WritePropertyName("packageEvidenceHash");
        WriteCanonicalHash(writer, envelope.PackageEvidenceHash);
        writer.WriteEndObject();
    }

    private static void WriteCanonicalHash(Utf8JsonWriter writer, Abstractions.CanonicalHashing.CanonicalHash hash)
    {
        writer.WriteStartObject();
        writer.WriteString("algorithm", hash.Algorithm);
        writer.WriteString("algorithmVersion", hash.AlgorithmVersion);
        writer.WriteString("artifactKind", hash.ArtifactKind);
        writer.WriteString("descriptorKind", hash.DescriptorKind);
        writer.WriteString("scope", hash.Scope);
        writer.WriteString("purpose", hash.Purpose);
        writer.WriteString("contractVersion", hash.ContractVersion);
        writer.WriteString("canonicalShapeVersion", hash.CanonicalShapeVersion);
        writer.WriteString("value", hash.Value);
        writer.WriteEndObject();
    }
}
