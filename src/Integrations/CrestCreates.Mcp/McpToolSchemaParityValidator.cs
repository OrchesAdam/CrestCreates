using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Mcp;

public sealed class McpToolSchemaParityValidator
{
    public void ValidateInput(SchemaDescriptor schema, JsonTypeInfo typeInfo)
        => Validate(schema, typeInfo, input: true);

    public void ValidateOutput(SchemaDescriptor schema, JsonTypeInfo typeInfo)
        => Validate(schema, typeInfo, input: false);

    private static void Validate(SchemaDescriptor schema, JsonTypeInfo typeInfo, bool input)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
            throw new McpToolConfigurationException("MCP115", "MCP JSON contract root must be an object.");

        var properties = typeInfo.Properties
            .Where(property => input ? property.Set is not null : property.Get is not null)
            .ToDictionary(property => property.Name, StringComparer.Ordinal);
        foreach (var field in schema.Fields)
        {
            if (!properties.TryGetValue(field.Name, out var property)
                || input && property.Set is null
                || !input && property.Get is null)
            {
                throw new McpToolConfigurationException("MCP108", "Schema and JSON contract properties do not match.");
            }

            // For input contracts, the Schema remains the authoritative presence
            // rule so that a missing property can materialize its DTO and reach
            // Capability ValidationMiddleware. A JsonTypeInfo-required property
            // must still not weaken an optional Schema field. Output contracts
            // must match the serializer's declared presence metadata exactly.
            if (input
                ? property.IsRequired && !field.IsRequired
                : property.IsRequired != field.IsRequired)
                throw new McpToolConfigurationException("MCP108", "Schema and JSON requiredness do not match.");

            var nullable = input ? property.IsSetNullable : property.IsGetNullable;
            if (nullable != field.IsNullable)
                throw new McpToolConfigurationException("MCP108", "Schema and JSON nullability do not match.");

            if (!MatchesType(field, property.PropertyType))
                throw new McpToolConfigurationException("MCP108", "Schema and JSON property types do not match.");
        }

        var schemaNames = schema.Fields.Select(field => field.Name).ToHashSet(StringComparer.Ordinal);
        if (properties.Keys.Any(name => !schemaNames.Contains(name)))
            throw new McpToolConfigurationException("MCP108", "JSON contract contains a property not declared by the Schema.");
    }

    private static bool MatchesType(SchemaFieldDescriptor field, Type propertyType)
    {
        propertyType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (!field.IsCollection)
            return MatchesScalar(field.FieldType, propertyType);

        var elementType = GetCollectionElementType(propertyType);
        return elementType is not null && MatchesScalar(field.CollectionElementType, elementType);
    }

    private static Type? GetCollectionElementType(Type type)
    {
        if (type.IsArray)
            return type.GetElementType();
        if (!type.IsGenericType)
            return null;
        var definition = type.GetGenericTypeDefinition();
        if (definition != typeof(List<>)
            && definition != typeof(IList<>)
            && definition != typeof(IReadOnlyList<>)
            && definition != typeof(ICollection<>)
            && definition != typeof(IReadOnlyCollection<>)
            && definition != typeof(IEnumerable<>))
            return null;
        return type.GetGenericArguments()[0];
    }

    private static bool MatchesScalar(string? token, Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (!SchemaScalarTypes.TryResolve(token, out var kind))
            return false;
        return kind switch
        {
            SchemaScalarKind.String => type == typeof(string),
            SchemaScalarKind.Boolean => type == typeof(bool),
            SchemaScalarKind.Int32 => type == typeof(int),
            SchemaScalarKind.Int64 => type == typeof(long),
            SchemaScalarKind.Decimal => type == typeof(decimal),
            SchemaScalarKind.Double => type == typeof(double),
            SchemaScalarKind.Guid => type == typeof(Guid),
            SchemaScalarKind.Date => type == typeof(DateOnly),
            SchemaScalarKind.DateTime => type == typeof(DateTime) || type == typeof(DateTimeOffset),
            _ => false
        };
    }
}
