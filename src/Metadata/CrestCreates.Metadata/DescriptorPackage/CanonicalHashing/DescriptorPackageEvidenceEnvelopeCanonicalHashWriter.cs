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
    private static class Fields
    {
        public const string PackageId = "packageId";
        public const string PackageVersion = "packageVersion";
        public const string CreatedAt = "createdAt";
        public const string CreatedBy = "createdBy";
        public const string Source = "source";
        public const string PackageManifestHash = "packageManifestHash";
        public const string PackageEvidenceHash = "packageEvidenceHash";
        public const string Algorithm = "algorithm";
        public const string AlgorithmVersion = "algorithmVersion";
        public const string ArtifactKind = "artifactKind";
        public const string DescriptorKind = "descriptorKind";
        public const string Scope = "scope";
        public const string Purpose = "purpose";
        public const string ContractVersion = "contractVersion";
        public const string CanonicalShapeVersion = "canonicalShapeVersion";
        public const string Value = "value";
    }

    public static void WritePayload(Utf8JsonWriter writer, DescriptorPackageEvidenceEnvelope envelope)
    {
        writer.WriteStartObject();
        writer.WriteString(Fields.PackageId, envelope.PackageId);
        writer.WriteString(Fields.PackageVersion, envelope.PackageVersion);
        writer.WriteString(Fields.CreatedAt, envelope.CreatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        writer.WriteString(Fields.CreatedBy, envelope.CreatedBy);
        writer.WriteString(Fields.Source, envelope.Source);
        writer.WritePropertyName(Fields.PackageManifestHash);
        WriteCanonicalHash(writer, envelope.PackageManifestHash);
        writer.WritePropertyName(Fields.PackageEvidenceHash);
        WriteCanonicalHash(writer, envelope.PackageEvidenceHash);
        writer.WriteEndObject();
    }

    private static void WriteCanonicalHash(Utf8JsonWriter writer, Abstractions.CanonicalHashing.CanonicalHash hash)
    {
        writer.WriteStartObject();
        writer.WriteString(Fields.Algorithm, hash.Algorithm);
        writer.WriteString(Fields.AlgorithmVersion, hash.AlgorithmVersion);
        writer.WriteString(Fields.ArtifactKind, hash.ArtifactKind);
        writer.WriteString(Fields.DescriptorKind, hash.DescriptorKind);
        writer.WriteString(Fields.Scope, hash.Scope);
        writer.WriteString(Fields.Purpose, hash.Purpose);
        writer.WriteString(Fields.ContractVersion, hash.ContractVersion);
        writer.WriteString(Fields.CanonicalShapeVersion, hash.CanonicalShapeVersion);
        writer.WriteString(Fields.Value, hash.Value);
        writer.WriteEndObject();
    }
}
