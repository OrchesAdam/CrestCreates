using System.Text.Json.Serialization.Metadata;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Schema;

/// <summary>
/// Validates directional parity between a Schema and an application-owned
/// source-generated JSON contract.
/// </summary>
public sealed class SchemaJsonTypeInfoParityValidator
{
    public void ValidateInput(SchemaDescriptor schema, JsonTypeInfo typeInfo)
        => ValidateInput(schema, typeInfo, Array.Empty<SchemaDescriptor>());

    public void ValidateInput(
        SchemaDescriptor schema,
        JsonTypeInfo typeInfo,
        IReadOnlyList<SchemaDescriptor> referencedSchemas)
        => Validate(schema, typeInfo, referencedSchemas, input: true,
            new HashSet<(string Id, int Version)>());

    public void ValidateOutput(SchemaDescriptor schema, JsonTypeInfo typeInfo)
        => ValidateOutput(schema, typeInfo, Array.Empty<SchemaDescriptor>());

    public void ValidateOutput(
        SchemaDescriptor schema,
        JsonTypeInfo typeInfo,
        IReadOnlyList<SchemaDescriptor> referencedSchemas)
        => Validate(schema, typeInfo, referencedSchemas, input: false,
            new HashSet<(string Id, int Version)>());

    private static void Validate(
        SchemaDescriptor schema,
        JsonTypeInfo typeInfo,
        IReadOnlyList<SchemaDescriptor> referencedSchemas,
        bool input,
        HashSet<(string Id, int Version)> active)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            throw new SchemaJsonContractException(
                SchemaJsonContractViolation.RootContractNotObject,
                "JSON contract root must be an object.");
        }

        var properties = typeInfo.Properties
            .Where(property => input ? property.Set is not null : property.Get is not null)
            .ToDictionary(property => property.Name, StringComparer.Ordinal);
        foreach (var field in schema.Fields)
        {
            if (!properties.TryGetValue(field.Name, out var property)
                || input && property.Set is null
                || !input && property.Get is null)
            {
                throw new SchemaJsonContractException(
                    SchemaJsonContractViolation.JsonPropertyMismatch,
                    "Schema and JSON contract properties do not match.");
            }

            // Input presence belongs to Schema validation. JsonTypeInfo.IsRequired
            // would make STJ reject a missing property before the execution pipeline.
            // Output presence must match the serializer contract exactly.
            if (input ? property.IsRequired : property.IsRequired != field.IsRequired)
            {
                throw new SchemaJsonContractException(
                    SchemaJsonContractViolation.RequirednessMismatch,
                    input
                        ? $"Input JSON contract must not declare '{field.Name}' as required; presence belongs to Schema validation."
                        : $"Schema and JSON requiredness do not match for '{field.Name}'.");
            }

            var nullable = input ? property.IsSetNullable : property.IsGetNullable;
            if (nullable != field.IsNullable)
            {
                throw new SchemaJsonContractException(
                    SchemaJsonContractViolation.NullabilityMismatch,
                    $"Schema and JSON nullability do not match for '{field.Name}'.");
            }

            if (!MatchesType(field, property.PropertyType))
            {
                throw new SchemaJsonContractException(
                    SchemaJsonContractViolation.PropertyTypeMismatch,
                    $"Schema and JSON property types do not match for '{field.Name}'.");
            }

            if (field.ObjectSchema is { } nestedReference)
            {
                var resolver = referencedSchemas
                    .GroupBy(item => (item.Id, item.Version))
                    .ToDictionary(group => group.Key, group => group.First());
                if (!resolver.TryGetValue((nestedReference.Id, nestedReference.Version), out var nestedSchema))
                {
                    throw new SchemaJsonContractException(
                        SchemaJsonContractViolation.NestedSchemaNotFound,
                        "Nested Schema reference is not present in the trusted resolver snapshot.");
                }

                var nestedType = field.IsCollection
                    ? GetCollectionElementType(property.PropertyType)
                    : Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                if (nestedType is null)
                    throw new SchemaJsonContractException(
                        SchemaJsonContractViolation.PropertyTypeMismatch,
                        "Nested collection does not expose an element type.");

                var nestedInfo = typeInfo.Options.GetTypeInfo(nestedType);
                if (nestedInfo is null)
                    throw new SchemaJsonContractException(
                        SchemaJsonContractViolation.NestedSchemaNotFound,
                        "Nested JSON contract metadata is unavailable.");

                var key = (nestedSchema.Id, nestedSchema.Version);
                if (!active.Add(key))
                    throw new SchemaJsonContractException(
                        SchemaJsonContractViolation.NestedSchemaCycle,
                        "Nested Schema contract graph contains a cycle.");
                Validate(nestedSchema, nestedInfo, referencedSchemas, input, active);
                active.Remove(key);
            }
        }

        var schemaNames = schema.Fields.Select(field => field.Name).ToHashSet(StringComparer.Ordinal);
        if (properties.Keys.Any(name => !schemaNames.Contains(name)))
        {
            throw new SchemaJsonContractException(
                SchemaJsonContractViolation.AdditionalJsonProperty,
                "JSON contract contains a property not declared by the Schema.");
        }
    }

    private static bool MatchesType(SchemaFieldDescriptor field, Type propertyType)
    {
        propertyType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            if (field.ObjectSchema is not null)
            {
                if (field.IsCollection)
                {
                var nestedElementType = GetCollectionElementType(propertyType);
                return nestedElementType is not null && !SchemaScalarTypes.TryResolve(field.CollectionElementType, out _);
            }
            return !SchemaScalarTypes.TryResolve(field.FieldType, out _)
                && propertyType != typeof(string);
        }
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
            // Closed Tool enums use stable string converters on the wire; the
            // Schema contract therefore describes both string properties and
            // enum CLR properties as the string scalar shape.
            SchemaScalarKind.String => type == typeof(string) || type.IsEnum,
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
