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
        public const string FormatVersion = "formatVersion";
        public const string PackageId = "packageId";
        public const string PackageVersion = "packageVersion";
        public const string Name = "name";
        public const string CreatedAt = "createdAt";
        public const string CreatedBy = "createdBy";
        public const string Source = "source";
        public const string DescriptorCount = "descriptorCount";
        public const string DescriptorEntries = "descriptorEntries";
        public const string Namespace = "namespace";
        public const string Id = "id";
        public const string Version = "version";
        public const string Kind = "kind";
        public const string State = "state";
        public const string ContractHash = "contractHash";
        public const string DefinitionHash = "definitionHash";
        public const string SupersededById = "supersededById";
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
