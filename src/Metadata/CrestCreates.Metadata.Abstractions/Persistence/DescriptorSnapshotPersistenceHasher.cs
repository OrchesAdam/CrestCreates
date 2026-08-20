using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;

namespace CrestCreates.Metadata.Abstractions.Persistence;

/// <summary>
/// Computes the provider-neutral immutable identity for a descriptor snapshot.
/// The projection is deliberately independent of provider JSON serializers and
/// uses a fixed canonical writer plus normalized collection ordering.
/// </summary>
public sealed class DescriptorSnapshotPersistenceHasher : IDescriptorSnapshotPersistenceHasher
{
    public const string Profile = "descriptor-snapshot-persistence-v1";

    public DescriptorSnapshotPersistenceHash Compute(DescriptorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            WriteCanonical(writer, snapshot);
        }

        return new DescriptorSnapshotPersistenceHash(
            "SHA-256",
            Profile,
            Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan)).ToLowerInvariant());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, DescriptorSnapshot snapshot)
    {
        writer.WriteStartObject();
        writer.WriteString("snapshotId", snapshot.SnapshotId);
        writer.WriteString("packageId", snapshot.PackageId);
        writer.WriteString("packageVersion", snapshot.PackageVersion);
        writer.WriteString("createdAt", snapshot.CreatedAt.ToString("O", CultureInfo.InvariantCulture));

        writer.WriteStartArray("descriptors");
        foreach (var entry in snapshot.Descriptors
                     .OrderBy(x => x.Ref.Namespace, StringComparer.Ordinal)
                     .ThenBy(x => x.Ref.Id, StringComparer.Ordinal)
                     .ThenBy(x => x.Ref.Version ?? int.MinValue)
                     .ThenBy(x => x.DescriptorName, StringComparer.Ordinal)
                     .ThenBy(x => (int)x.Kind)
                     .ThenBy(x => (int)x.State)
                     .ThenBy(x => x.ContractHash, StringComparer.Ordinal)
                     .ThenBy(x => x.DefinitionHash, StringComparer.Ordinal)
                     .ThenBy(x => x.SupersededById, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            WriteDescriptorRef(writer, "ref", entry.Ref);
            writer.WriteString("descriptorName", entry.DescriptorName);
            writer.WriteNumber("kind", (int)entry.Kind);
            writer.WriteNumber("state", (int)entry.State);
            writer.WriteString("contractHash", entry.ContractHash);
            writer.WriteString("definitionHash", entry.DefinitionHash);
            WriteNullableString(writer, "supersededById", entry.SupersededById);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteStartArray("relationships");
        foreach (var relationship in snapshot.Relationships
                     .OrderBy(x => x.From.Namespace, StringComparer.Ordinal)
                     .ThenBy(x => x.From.Id, StringComparer.Ordinal)
                     .ThenBy(x => x.From.Version ?? int.MinValue)
                     .ThenBy(x => x.To.Namespace, StringComparer.Ordinal)
                     .ThenBy(x => x.To.Id, StringComparer.Ordinal)
                     .ThenBy(x => x.To.Version ?? int.MinValue)
                     .ThenBy(x => (int)x.Kind)
                     .ThenBy(x => x.Role, StringComparer.Ordinal)
                     .ThenBy(x => x.SourcePath, StringComparer.Ordinal)
                     .ThenBy(x => (int)x.Strength)
                     .ThenBy(x => x.IsRuntimeBinding))
        {
            writer.WriteStartObject();
            WriteDescriptorRef(writer, "from", relationship.From);
            WriteDescriptorRef(writer, "to", relationship.To);
            writer.WriteNumber("kind", (int)relationship.Kind);
            WriteNullableString(writer, "role", relationship.Role);
            WriteNullableString(writer, "sourcePath", relationship.SourcePath);
            writer.WriteNumber("strength", (int)relationship.Strength);
            writer.WriteBoolean("isRuntimeBinding", relationship.IsRuntimeBinding);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteDescriptorRef(Utf8JsonWriter writer, string propertyName, DescriptorRef value)
    {
        writer.WriteStartObject(propertyName);
        writer.WriteString("namespace", value.Namespace);
        writer.WriteString("id", value.Id);
        if (value.Version is int version)
            writer.WriteNumber("version", version);
        else
            writer.WriteNull("version");
        writer.WriteEndObject();
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string propertyName, string? value)
    {
        if (value is null)
            writer.WriteNull(propertyName);
        else
            writer.WriteString(propertyName, value);
    }
}
