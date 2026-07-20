using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Schema;

internal sealed class SchemaJsonNestedContractProjector
{
    private const int MaximumDepth = 4;
    private const int MaximumReferencedSchemas = 64;
    private const int MaximumFields = 256;

    public JsonElement ProjectObject(
        SchemaDescriptor? schema,
        IReadOnlyList<SchemaDescriptor> referencedSchemas)
    {
        if (schema?.ValidationRules.Count > 0)
            throw Contract(SchemaJsonContractViolation.ValidationRulesUnsupported);

        var resolver = new Dictionary<(string Id, int Version), SchemaDescriptor>();
        foreach (var referenced in referencedSchemas)
        {
            var key = (referenced.Id, referenced.Version);
            if (!resolver.TryAdd(key, referenced))
                throw Contract(SchemaJsonContractViolation.NestedSchemaReferenceInvalid);
        }
        var graph = new GraphState(resolver);
        graph.ValidateRoot(schema);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            WriteSchemaBody(writer, schema, graph, 0);

            if (graph.Referenced.Count > 0)
            {
                writer.WritePropertyName("$defs");
                writer.WriteStartObject();
                foreach (var nested in graph.Referenced.Values
                             .OrderBy(item => DefinitionKey(item), StringComparer.Ordinal))
                {
                    writer.WritePropertyName(DefinitionKey(nested));
                    writer.WriteStartObject();
                    WriteSchemaBody(writer, nested, graph, graph.DepthBySchema[(nested.Id, nested.Version)]);
                    writer.WriteEndObject();
                }
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static void WriteSchemaBody(
        Utf8JsonWriter writer,
        SchemaDescriptor? schema,
        GraphState graph,
        int depth)
    {
        writer.WriteString("type", "object");
        writer.WritePropertyName("properties");
        writer.WriteStartObject();

        foreach (var field in (schema?.Fields ?? Array.Empty<SchemaFieldDescriptor>())
                     .OrderBy(field => field.Name, StringComparer.Ordinal))
        {
            writer.WritePropertyName(field.Name);
            writer.WriteStartObject();
            WriteField(writer, field, graph, depth);
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
    }

    private static void WriteField(
        Utf8JsonWriter writer,
        SchemaFieldDescriptor field,
        GraphState graph,
        int depth)
    {
        ValidateField(field);

        if (field.ObjectSchema is { } nested)
        {
            var target = graph.Resolve(nested, depth + 1);
            if (field.IsCollection)
            {
                WriteType(writer, "array", field.IsNullable);
                writer.WritePropertyName("items");
                writer.WriteStartObject();
                writer.WriteString("$ref", "#/$defs/" + DefinitionKey(target));
                writer.WriteEndObject();
            }
            else if (field.IsNullable)
            {
                writer.WritePropertyName("anyOf");
                writer.WriteStartArray();
                writer.WriteStartObject();
                writer.WriteString("$ref", "#/$defs/" + DefinitionKey(target));
                writer.WriteEndObject();
                writer.WriteStartObject();
                writer.WriteString("type", "null");
                writer.WriteEndObject();
                writer.WriteEndArray();
            }
            else
            {
                writer.WriteString("$ref", "#/$defs/" + DefinitionKey(target));
            }

            return;
        }

        if (field.IsCollection)
        {
            WriteType(writer, "array", field.IsNullable);
            writer.WritePropertyName("items");
            writer.WriteStartObject();
            WriteScalar(writer, field.CollectionElementType!, false, field);
            writer.WriteEndObject();
        }
        else
        {
            WriteScalar(writer, field.FieldType, field.IsNullable, field);
        }
    }

    private static void ValidateField(SchemaFieldDescriptor field)
    {
        if (field.ObjectSchema is not null)
        {
            if (!string.Equals(field.FieldType, "object", StringComparison.Ordinal)
                || field.CollectionElementType is not null
                || field.Pattern is not null
                || field.MinLength is not null
                || field.MaxLength is not null
                || field.MinValue is not null
                || field.MaxValue is not null)
                throw Contract(SchemaJsonContractViolation.NestedSchemaReferenceInvalid);
            return;
        }

        if (string.Equals(field.FieldType, "object", StringComparison.Ordinal))
            throw Contract(SchemaJsonContractViolation.NestedSchemaReferenceInvalid);

        if (string.IsNullOrWhiteSpace(field.Name))
            throw Contract(SchemaJsonContractViolation.FieldIdentityInvalid);
        if (!string.IsNullOrEmpty(field.Pattern))
            throw Contract(SchemaJsonContractViolation.PatternUnsupported);
        if (field.MinLength is < 0 || field.MaxLength is < 0 || field.MinLength > field.MaxLength)
            throw Contract(SchemaJsonContractViolation.LengthConstraintInvalid);
        if (field.MinValue.HasValue && !double.IsFinite(field.MinValue.Value)
            || field.MaxValue.HasValue && !double.IsFinite(field.MaxValue.Value)
            || field.MinValue > field.MaxValue)
            throw Contract(SchemaJsonContractViolation.NumericConstraintInvalid);

        var token = field.IsCollection ? field.CollectionElementType : field.FieldType;
        if (string.IsNullOrWhiteSpace(token))
            throw Contract(SchemaJsonContractViolation.FieldTypeMissing);
        var (type, _) = ResolveToken(token);
        if (field.IsCollection && field.CollectionElementType is null)
            throw Contract(SchemaJsonContractViolation.CollectionElementTypeMissing);
        if (type == "boolean" && (field.MinLength.HasValue || field.MaxLength.HasValue || field.MinValue.HasValue || field.MaxValue.HasValue)
            || type == "string" && (field.MinValue.HasValue || field.MaxValue.HasValue)
            || (type == "integer" || type == "number") && (field.MinLength.HasValue || field.MaxLength.HasValue))
            throw Contract(SchemaJsonContractViolation.ConstraintNotApplicable);
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

    private static (string Type, string? Format) ResolveToken(string token)
    {
        if (!SchemaScalarTypes.TryResolve(token, out var kind))
            throw Contract(SchemaJsonContractViolation.ScalarTypeUnsupported);

        return kind switch
        {
            SchemaScalarKind.String => ("string", null),
            SchemaScalarKind.Boolean => ("boolean", null),
            SchemaScalarKind.Int32 or SchemaScalarKind.Int64 => ("integer", null),
            SchemaScalarKind.Decimal or SchemaScalarKind.Double => ("number", null),
            SchemaScalarKind.Guid => ("string", "uuid"),
            SchemaScalarKind.Date => ("string", "date"),
            SchemaScalarKind.DateTime => ("string", "date-time"),
            _ => throw Contract(SchemaJsonContractViolation.ScalarTypeUnsupported)
        };
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

    private static long EffectiveIntegralBound(double? configured, long minimum, long maximum, bool lower)
    {
        if (!configured.HasValue)
            return lower ? minimum : maximum;
        var value = checked((decimal)configured.Value);
        if (decimal.Truncate(value) != value || value < minimum || value > maximum)
            throw Contract(SchemaJsonContractViolation.IntegerConstraintInvalid);
        return checked((long)value);
    }

    private static void WriteLongBound(Utf8JsonWriter writer, string name, double? configured, long minimum, long maximum, bool lower)
    {
        writer.WriteNumber(name, EffectiveIntegralBound(configured, minimum, maximum, lower));
    }

    private static string DefinitionKey(SchemaDescriptor schema)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("Namespace", schema.Namespace);
            writer.WriteString("Id", schema.Id);
            writer.WriteNumber("Version", schema.Version);
            writer.WriteEndObject();
        }

        return "schema-" + Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan)).ToLowerInvariant();
    }

    private static SchemaJsonContractException Contract(SchemaJsonContractViolation violation)
        => new(violation, "Schema nested JSON contract is invalid.");

    private sealed class GraphState
    {
        private readonly IReadOnlyDictionary<(string Id, int Version), SchemaDescriptor> _resolver;
        private readonly HashSet<(string Id, int Version)> _active = new();

        public GraphState(IReadOnlyDictionary<(string Id, int Version), SchemaDescriptor> resolver)
            => _resolver = resolver;

        public Dictionary<(string Id, int Version), SchemaDescriptor> Referenced { get; } = new();
        public Dictionary<(string Id, int Version), int> DepthBySchema { get; } = new();
        public int FieldCount { get; private set; }

        public void ValidateRoot(SchemaDescriptor? schema)
        {
            if (schema is null)
                return;
            FieldCount += schema.Fields.Count;
            if (FieldCount > MaximumFields)
                throw Contract(SchemaJsonContractViolation.NestedSchemaGraphLimitExceeded);
            ValidateNestedReferences(schema, 0);
        }

        public SchemaDescriptor Resolve(VersionedDescriptorRef<SchemaDescriptor> reference, int depth)
        {
            if (reference.SelectionMode != VersionSelectionMode.Exact || reference.Version <= 0 || string.IsNullOrWhiteSpace(reference.Id))
                throw Contract(SchemaJsonContractViolation.NestedSchemaReferenceInvalid);
            if (!_resolver.TryGetValue((reference.Id, reference.Version), out var schema))
                throw Contract(SchemaJsonContractViolation.NestedSchemaNotFound);
            if (depth > MaximumDepth)
                throw Contract(SchemaJsonContractViolation.NestedSchemaGraphLimitExceeded);

            var key = (schema.Id, schema.Version);
            if (!_active.Add(key))
                throw Contract(SchemaJsonContractViolation.NestedSchemaCycle);

            if (Referenced.ContainsKey(key))
            {
                _active.Remove(key);
                return schema;
            }

            FieldCount += schema.Fields.Count;
            if (FieldCount > MaximumFields)
                throw Contract(SchemaJsonContractViolation.NestedSchemaGraphLimitExceeded);
            if (!Referenced.ContainsKey(key) && Referenced.Count >= MaximumReferencedSchemas)
                throw Contract(SchemaJsonContractViolation.NestedSchemaGraphLimitExceeded);

            Referenced[key] = schema;
            DepthBySchema[key] = Math.Max(DepthBySchema.GetValueOrDefault(key), depth);
            ValidateNestedReferences(schema, depth);
            _active.Remove(key);
            return schema;
        }

        private void ValidateNestedReferences(SchemaDescriptor schema, int depth)
        {
            foreach (var directReference in schema.References)
                Resolve(directReference, depth + 1);

            foreach (var field in schema.Fields)
            {
                ValidateField(field);
                if (field.ObjectSchema is { } reference)
                    Resolve(reference, depth + 1);
            }
        }
    }
}
