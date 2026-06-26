using System.Globalization;
using System.Text.Json;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
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
        public const string PackageId = nameof(DescriptorPackageEvidenceEnvelope.PackageId);
        public const string PackageVersion = nameof(DescriptorPackageEvidenceEnvelope.PackageVersion);
        public const string CreatedAt = nameof(DescriptorPackageEvidenceEnvelope.CreatedAt);
        public const string CreatedBy = nameof(DescriptorPackageEvidenceEnvelope.CreatedBy);
        public const string Source = nameof(DescriptorPackageEvidenceEnvelope.Source);
        public const string PackageManifestHash = nameof(DescriptorPackageEvidenceEnvelope.PackageManifestHash);
        public const string PackageEvidenceHash = nameof(DescriptorPackageEvidenceEnvelope.PackageEvidenceHash);
        public const string Algorithm = nameof(CanonicalHash.Algorithm);
        public const string AlgorithmVersion = nameof(CanonicalHash.AlgorithmVersion);
        public const string ArtifactKind = nameof(CanonicalHash.ArtifactKind);
        public const string DescriptorKind = nameof(CanonicalHash.DescriptorKind);
        public const string Scope = nameof(CanonicalHash.Scope);
        public const string Purpose = nameof(CanonicalHash.Purpose);
        public const string ContractVersion = nameof(CanonicalHash.ContractVersion);
        public const string CanonicalShapeVersion = nameof(CanonicalHash.CanonicalShapeVersion);
        public const string Value = nameof(CanonicalHash.Value);
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

    private static void WriteCanonicalHash(Utf8JsonWriter writer, CanonicalHash hash)
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
