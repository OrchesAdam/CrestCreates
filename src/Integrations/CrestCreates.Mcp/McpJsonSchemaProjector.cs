using System.Text.Json;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Mcp;

public interface IMcpJsonSchemaProjector
{
    JsonElement ProjectInput(SchemaDescriptor? schema);

    JsonElement? ProjectOutput(SchemaDescriptor? schema);
}

public sealed class McpJsonSchemaProjector : IMcpJsonSchemaProjector
{
    public JsonElement ProjectInput(SchemaDescriptor? schema) => Project(schema);

    public JsonElement? ProjectOutput(SchemaDescriptor? schema)
        => schema is null ? null : Project(schema);

    private static JsonElement Project(SchemaDescriptor? schema)
    {
        if (schema is not null)
            ValidateSchema(schema);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "object");
            writer.WritePropertyName("properties");
            writer.WriteStartObject();
            foreach (var field in (schema?.Fields ?? Array.Empty<SchemaFieldDescriptor>())
                         .OrderBy(field => field.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(field.Name);
                writer.WriteStartObject();
                if (field.IsCollection)
                {
                    WriteType(writer, "array", field.IsNullable);
                    writer.WritePropertyName("items");
                    writer.WriteStartObject();
                    WriteScalar(writer, field.CollectionElementType!, nullable: false, field);
                    writer.WriteEndObject();
                }
                else
                {
                    WriteScalar(writer, field.FieldType, field.IsNullable, field);
                }
                writer.WriteEndObject();
            }
            writer.WriteEndObject();

            var required = (schema?.Fields ?? Array.Empty<SchemaFieldDescriptor>())
                .Where(field => field.IsRequired)
                .Select(field => field.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            if (required.Length > 0)
            {
                writer.WritePropertyName("required");
                writer.WriteStartArray();
                foreach (var name in required)
                    writer.WriteStringValue(name);
                writer.WriteEndArray();
            }
            writer.WriteBoolean("additionalProperties", false);
            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static void WriteScalar(
        Utf8JsonWriter writer,
        string token,
        bool nullable,
        SchemaFieldDescriptor field)
    {
        var (type, format) = ResolveToken(token);
        WriteType(writer, type, nullable);
        if (format is not null)
            writer.WriteString("format", format);

        if (type == "string")
        {
            if (field.MinLength.HasValue)
                writer.WriteNumber("minLength", field.MinLength.Value);
            if (field.MaxLength.HasValue)
                writer.WriteNumber("maxLength", field.MaxLength.Value);
        }
        else if (token == "int")
        {
            writer.WriteNumber("minimum", EffectiveIntegralBound(field.MinValue, int.MinValue, int.MaxValue, true));
            writer.WriteNumber("maximum", EffectiveIntegralBound(field.MaxValue, int.MinValue, int.MaxValue, false));
        }
        else if (token == "long")
        {
            WriteLongBound(writer, "minimum", field.MinValue, long.MinValue, long.MaxValue, true);
            WriteLongBound(writer, "maximum", field.MaxValue, long.MinValue, long.MaxValue, false);
        }
        else if (type == "number")
        {
            if (field.MinValue.HasValue)
                writer.WriteNumber("minimum", field.MinValue.Value);
            if (field.MaxValue.HasValue)
                writer.WriteNumber("maximum", field.MaxValue.Value);
        }
    }

    private static void WriteType(Utf8JsonWriter writer, string type, bool nullable)
    {
        if (!nullable)
        {
            writer.WriteString("type", type);
            return;
        }

        writer.WritePropertyName("type");
        writer.WriteStartArray();
        writer.WriteStringValue(type);
        writer.WriteStringValue("null");
        writer.WriteEndArray();
    }

    private static (string Type, string? Format) ResolveToken(string token)
    {
        if (!SchemaScalarTypes.TryResolve(token, out var kind))
            throw new McpToolConfigurationException("MCP113", "Schema field type is not supported by MCP projection.");
        return kind switch
        {
            SchemaScalarKind.String => ("string", null),
            SchemaScalarKind.Boolean => ("boolean", null),
            SchemaScalarKind.Int32 or SchemaScalarKind.Int64 => ("integer", null),
            SchemaScalarKind.Decimal or SchemaScalarKind.Double => ("number", null),
            SchemaScalarKind.Guid => ("string", "uuid"),
            SchemaScalarKind.Date => ("string", "date"),
            SchemaScalarKind.DateTime => ("string", "date-time"),
            _ => throw new McpToolConfigurationException("MCP113", "Schema field type is not supported by MCP projection.")
        };
    }

    private static long EffectiveIntegralBound(double? configured, long minimum, long maximum, bool lower)
    {
        if (!configured.HasValue)
            return lower ? minimum : maximum;
        var value = checked((decimal)configured.Value);
        if (decimal.Truncate(value) != value || value < minimum || value > maximum)
            throw new McpToolConfigurationException("MCP121", "Schema integer constraint is invalid.");
        return checked((long)value);
    }

    private static void WriteLongBound(
        Utf8JsonWriter writer,
        string name,
        double? configured,
        long minimum,
        long maximum,
        bool lower)
    {
        if (!configured.HasValue)
            writer.WriteNumber(name, lower ? minimum : maximum);
        else
            writer.WriteNumber(name, EffectiveIntegralBound(configured, minimum, maximum, lower));
    }

    private static void ValidateSchema(SchemaDescriptor schema)
    {
        if (schema.ValidationRules.Count > 0)
            throw new McpToolConfigurationException("MCP111", "Schema validation rules cannot be projected to MCP.");
        if (schema.References.Count > 0)
            throw new McpToolConfigurationException("MCP112", "Schema references cannot be projected to MCP.");

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in schema.Fields)
        {
            if (string.IsNullOrWhiteSpace(field.Name) || !names.Add(field.Name))
                throw new McpToolConfigurationException("MCP121", "Schema field identity is invalid.");
            if (!string.IsNullOrEmpty(field.Pattern))
                throw new McpToolConfigurationException("MCP120", "Schema patterns are not portable to MCP JSON Schema.");
            if (field.MinLength is < 0 || field.MaxLength is < 0 || field.MinLength > field.MaxLength)
                throw new McpToolConfigurationException("MCP121", "Schema length constraint is invalid.");
            if (field.MinValue.HasValue && !double.IsFinite(field.MinValue.Value)
                || field.MaxValue.HasValue && !double.IsFinite(field.MaxValue.Value)
                || field.MinValue > field.MaxValue)
                throw new McpToolConfigurationException("MCP121", "Schema numeric constraint is invalid.");

            var token = field.IsCollection ? field.CollectionElementType : field.FieldType;
            if (string.IsNullOrWhiteSpace(token))
                throw new McpToolConfigurationException("MCP113", "Schema field type is missing.");
            var (type, _) = ResolveToken(token);
            if (field.IsCollection && field.CollectionElementType is null)
                throw new McpToolConfigurationException("MCP113", "Collection element type is missing.");
            if (type == "boolean" && (field.MinLength.HasValue || field.MaxLength.HasValue || field.MinValue.HasValue || field.MaxValue.HasValue)
                || type == "string" && (field.MinValue.HasValue || field.MaxValue.HasValue)
                || (type == "integer" || type == "number") && (field.MinLength.HasValue || field.MaxLength.HasValue))
                throw new McpToolConfigurationException("MCP121", "Schema constraint is not applicable to its field type.");
        }
    }
}

public sealed class McpToolConfigurationException : Exception
{
    public McpToolConfigurationException(string code, string message) : base(message) => Code = code;

    public string Code { get; }
}
