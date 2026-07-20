using System.Text.Json;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Metadata.CanonicalHashing;

/// <summary>
/// Schema v3 projection used only when a schema contains bounded nested object
/// references. The generated v2 profiles remain untouched so existing flat
/// contract and definition bytes stay stable.
/// </summary>
internal static class SchemaNestedCanonicalHashProjection
{
    public const string ContractShapeVersion = "schema-contract-hash-v3";
    public const string DefinitionShapeVersion = "schema-definition-hash-v3";

    public static CanonicalHashProjectionResult Create(
        SchemaDescriptor schema,
        CanonicalHashScope scope,
        bool definition,
        string contractVersion,
        string algorithmVersion)
    {
        var scopeString = CanonicalHashScopeNames.ToCanonicalString(scope);
        var shape = definition ? DefinitionShapeVersion : ContractShapeVersion;
        return CanonicalHashProjectionResult.Create(
            new CanonicalHashMetadata
            {
                ArtifactKind = CanonicalHashArtifactNames.Descriptor,
                DescriptorKind = DescriptorKindNames.Schema,
                Scope = scopeString,
                Purpose = definition
                    ? CanonicalHashPurposeNames.Definition
                    : CanonicalHashPurposeNames.Contract,
                CanonicalShapeVersion = shape,
                AlgorithmVersion = algorithmVersion,
                ContractVersion = contractVersion
            },
            writer => WriteEnvelope(writer, schema, scopeString, definition, shape,
                algorithmVersion, contractVersion));
    }

    private static void WriteEnvelope(
        Utf8JsonWriter writer,
        SchemaDescriptor schema,
        string scope,
        bool definition,
        string shape,
        string algorithmVersion,
        string contractVersion)
    {
        writer.WriteStartObject();
        writer.WriteString("ArtifactKind", CanonicalHashArtifactNames.Descriptor);
        writer.WriteString("DescriptorKind", DescriptorKindNames.Schema);
        writer.WriteString("Scope", scope);
        writer.WriteString("Purpose", definition
            ? CanonicalHashPurposeNames.Definition
            : CanonicalHashPurposeNames.Contract);
        writer.WriteString("ContractVersion", contractVersion);
        writer.WriteString("CanonicalShapeVersion", shape);
        writer.WriteString("AlgorithmVersion", algorithmVersion);
        writer.WritePropertyName("Payload");
        writer.WriteStartObject();
        WriteString(writer, "Id", schema.Id);
        WriteString(writer, "Name", schema.Name);
        writer.WriteNumber("Version", schema.Version);
        WriteString(writer, "ChangeKind", schema.ChangeKind.ToString());
        WriteString(writer, "State", schema.State.ToString());
        if (schema.SupersededById is null)
            writer.WriteNull("SupersededById");
        else
            WriteString(writer, "SupersededById", schema.SupersededById);

        writer.WritePropertyName("Fields");
        writer.WriteStartArray();
        foreach (var field in schema.Fields
                     .Where(field => definition || field.IsRequired)
                     .OrderBy(field => field.Name, StringComparer.Ordinal))
            WriteField(writer, field);
        writer.WriteEndArray();

        writer.WritePropertyName("References");
        writer.WriteStartArray();
        foreach (var reference in schema.References
                     .OrderBy(reference => reference.Id, StringComparer.Ordinal)
                     .ThenBy(reference => reference.Version))
            WriteReference(writer, reference);
        writer.WriteEndArray();

        if (definition)
        {
            writer.WritePropertyName("ValidationRules");
            writer.WriteStartArray();
            foreach (var rule in schema.ValidationRules
                         .OrderBy(rule => rule.Name, StringComparer.Ordinal)
                         .ThenBy(rule => rule.Expression, StringComparer.Ordinal)
                         .ThenBy(rule => rule.ErrorMessage, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                WriteString(writer, "Name", rule.Name);
                WriteString(writer, "Expression", rule.Expression);
                if (rule.ErrorMessage is null)
                    writer.WriteNull("ErrorMessage");
                else
                    WriteString(writer, "ErrorMessage", rule.ErrorMessage);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteField(Utf8JsonWriter writer, SchemaFieldDescriptor field)
    {
        writer.WriteStartObject();
        WriteString(writer, "Name", field.Name);
        WriteString(writer, "FieldType", field.FieldType);
        if (field.ObjectSchema is { } nested)
        {
            writer.WritePropertyName("ObjectSchema");
            WriteReferenceBody(writer, nested);
        }
        else
        {
            writer.WriteNull("ObjectSchema");
        }
        writer.WriteBoolean("IsRequired", field.IsRequired);
        writer.WriteBoolean("IsNullable", field.IsNullable);
        WriteNullableNumber(writer, "MaxLength", field.MaxLength);
        WriteNullableNumber(writer, "MinLength", field.MinLength);
        WriteNullableNumber(writer, "MaxValue", field.MaxValue);
        WriteNullableNumber(writer, "MinValue", field.MinValue);
        if (field.Pattern is null) writer.WriteNull("Pattern"); else WriteString(writer, "Pattern", field.Pattern);
        writer.WriteBoolean("IsCollection", field.IsCollection);
        if (field.CollectionElementType is null) writer.WriteNull("CollectionElementType");
        else WriteString(writer, "CollectionElementType", field.CollectionElementType);
        writer.WriteEndObject();
    }

    private static void WriteReference(
        Utf8JsonWriter writer,
        VersionedDescriptorRef<SchemaDescriptor> reference)
    {
        writer.WriteStartObject();
        WriteReferenceBody(writer, reference);
        writer.WriteEndObject();
    }

    private static void WriteReferenceBody(
        Utf8JsonWriter writer,
        VersionedDescriptorRef<SchemaDescriptor> reference)
    {
        WriteString(writer, "Id", reference.Id);
        writer.WriteNumber("Version", reference.Version);
    }

    private static void WriteString(Utf8JsonWriter writer, string name, string value)
        => writer.WriteString(name, value);

    private static void WriteNullableNumber(Utf8JsonWriter writer, string name, int? value)
    {
        if (value.HasValue) writer.WriteNumber(name, value.Value);
        else writer.WriteNull(name);
    }

    private static void WriteNullableNumber(Utf8JsonWriter writer, string name, double? value)
    {
        if (value.HasValue) writer.WriteNumber(name, value.Value);
        else writer.WriteNull(name);
    }
}
