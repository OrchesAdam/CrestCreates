using System.Globalization;
using System.Text.Json;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.DescriptorPackage.CanonicalHashing;

/// <summary>
/// Canonical JSON writer for DescriptorManifest.
/// Property order: formatVersion, packageId, packageVersion, name, createdAt, createdBy, source,
/// descriptorCount, descriptorEntries (sorted by namespace, id, version, kind, name).
/// </summary>
public static class DescriptorPackageManifestCanonicalHashWriter
{
    private static class Fields
    {
        public const string FormatVersion = nameof(DescriptorManifest.FormatVersion);
        public const string PackageId = nameof(DescriptorManifest.PackageId);
        public const string PackageVersion = nameof(DescriptorManifest.PackageVersion);
        public const string Name = nameof(DescriptorManifest.Name);
        public const string CreatedAt = nameof(DescriptorManifest.CreatedAt);
        public const string CreatedBy = nameof(DescriptorManifest.CreatedBy);
        public const string Source = nameof(DescriptorManifest.Source);
        public const string DescriptorCount = nameof(DescriptorManifest.DescriptorCount);
        public const string DescriptorEntries = nameof(DescriptorManifest.DescriptorEntries);
        public const string Namespace = nameof(DescriptorRef.Namespace);
        public const string Id = nameof(DescriptorRef.Id);
        public const string Version = nameof(DescriptorRef.Version);
        public const string Kind = nameof(DescriptorManifestEntry.Kind);
        public const string State = nameof(DescriptorManifestEntry.State);
        public const string ContractHash = nameof(DescriptorManifestEntry.ContractHash);
        public const string DefinitionHash = nameof(DescriptorManifestEntry.DefinitionHash);
        public const string SupersededById = nameof(DescriptorManifestEntry.SupersededById);
    }

    public static void WritePayload(Utf8JsonWriter writer, DescriptorManifest manifest)
    {
        writer.WriteStartObject();
        writer.WriteString(Fields.FormatVersion, manifest.FormatVersion);
        writer.WriteString(Fields.PackageId, manifest.PackageId);
        writer.WriteString(Fields.PackageVersion, manifest.PackageVersion);
        writer.WriteString(Fields.Name, manifest.Name);
        writer.WriteString(Fields.CreatedAt, manifest.CreatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        writer.WriteString(Fields.CreatedBy, manifest.CreatedBy);
        writer.WriteString(Fields.Source, manifest.Source);
        writer.WriteNumber(Fields.DescriptorCount, manifest.DescriptorCount);
        writer.WritePropertyName(Fields.DescriptorEntries);
        writer.WriteStartArray();
        foreach (var entry in manifest.DescriptorEntries
            .OrderBy(e => e.Ref.Namespace, StringComparer.Ordinal)
            .ThenBy(e => e.Ref.Id, StringComparer.Ordinal)
            .ThenBy(e => e.Ref.Version)
            .ThenBy(e => e.Kind)
            .ThenBy(e => e.Name, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString(Fields.Namespace, entry.Ref.Namespace);
            writer.WriteString(Fields.Id, entry.Ref.Id);
            if (entry.Ref.Version is null)
                writer.WriteNull(Fields.Version);
            else
                writer.WriteNumber(Fields.Version, entry.Ref.Version.Value);
            writer.WriteString(Fields.Kind, entry.Kind.ToString());
            writer.WriteString(Fields.Name, entry.Name);
            writer.WriteString(Fields.State, entry.State.ToString());
            writer.WriteString(Fields.ContractHash, entry.ContractHash);
            writer.WriteString(Fields.DefinitionHash, entry.DefinitionHash);
            writer.WriteString(Fields.SupersededById, entry.SupersededById);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
