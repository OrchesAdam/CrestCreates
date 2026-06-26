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
    public static void WritePayload(Utf8JsonWriter writer, DescriptorManifest manifest)
    {
        writer.WriteStartObject();
        writer.WriteString("formatVersion", manifest.FormatVersion);
        writer.WriteString("packageId", manifest.PackageId);
        writer.WriteString("packageVersion", manifest.PackageVersion);
        writer.WriteString("name", manifest.Name);
        writer.WriteString("createdAt", manifest.CreatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        writer.WriteString("createdBy", manifest.CreatedBy);
        writer.WriteString("source", manifest.Source);
        writer.WriteNumber("descriptorCount", manifest.DescriptorCount);
        writer.WritePropertyName("descriptorEntries");
        writer.WriteStartArray();
        foreach (var entry in manifest.DescriptorEntries
            .OrderBy(e => e.Ref.Namespace, StringComparer.Ordinal)
            .ThenBy(e => e.Ref.Id, StringComparer.Ordinal)
            .ThenBy(e => e.Ref.Version)
            .ThenBy(e => e.Kind)
            .ThenBy(e => e.Name, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("namespace", entry.Ref.Namespace);
            writer.WriteString("id", entry.Ref.Id);
            if (entry.Ref.Version is null)
                writer.WriteNull("version");
            else
                writer.WriteNumber("version", entry.Ref.Version.Value);
            writer.WriteString("kind", entry.Kind.ToString());
            writer.WriteString("name", entry.Name);
            writer.WriteString("state", entry.State.ToString());
            writer.WriteString("contractHash", entry.ContractHash);
            writer.WriteString("definitionHash", entry.DefinitionHash);
            writer.WriteString("supersededById", entry.SupersededById);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
