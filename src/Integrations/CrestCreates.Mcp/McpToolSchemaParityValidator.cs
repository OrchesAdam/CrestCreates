using System.Text.Json.Serialization.Metadata;
using CrestCreates.Schema;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Mcp;

/// <summary>
/// Validates that generated JSON type info matches schema declarations.
/// Root-only overloads delegate to closure-aware overloads with empty schema list.
/// </summary>
public sealed class McpToolSchemaParityValidator
{
    private static readonly SchemaJsonTypeInfoParityValidator Validator = new();

    // Root-only overloads (backward compat) — delegate directly to Validator,
    // not through the closure overloads to avoid any overload ambiguity.
    public void ValidateInput(SchemaDescriptor schema, JsonTypeInfo typeInfo)
        => Validate(() => Validator.ValidateInput(schema, typeInfo));

    public void ValidateOutput(SchemaDescriptor schema, JsonTypeInfo typeInfo)
        => Validate(() => Validator.ValidateOutput(schema, typeInfo));

    // Closure-aware overloads
    public void ValidateInput(SchemaDescriptor schema, JsonTypeInfo typeInfo, IReadOnlyList<SchemaDescriptor> referencedSchemas)
        => Validate(() => Validator.ValidateInput(schema, typeInfo, referencedSchemas));

    public void ValidateOutput(SchemaDescriptor schema, JsonTypeInfo typeInfo, IReadOnlyList<SchemaDescriptor> referencedSchemas)
        => Validate(() => Validator.ValidateOutput(schema, typeInfo, referencedSchemas));

    private static void Validate(Action validation)
    {
        try
        {
            validation();
        }
        catch (SchemaJsonContractException exception)
        {
            throw McpSchemaJsonContractExceptionMapper.Map(exception);
        }
    }
}
