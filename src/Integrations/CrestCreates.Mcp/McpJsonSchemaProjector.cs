using System.Text.Json;
using CrestCreates.Schema;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Mcp;

public interface IMcpJsonSchemaProjector
{
    // Root-only overloads (backward compat)
    JsonElement ProjectInput(SchemaDescriptor? schema);
    JsonElement? ProjectOutput(SchemaDescriptor? schema);

    // Closure-aware overloads
    JsonElement ProjectInput(SchemaDescriptor? schema, IReadOnlyList<SchemaDescriptor> referencedSchemas);
    JsonElement? ProjectOutput(SchemaDescriptor? schema, IReadOnlyList<SchemaDescriptor> referencedSchemas);
}

public sealed class McpJsonSchemaProjector : IMcpJsonSchemaProjector
{
    private static readonly SchemaJsonContractProjector Projector = new();

    // Root-only overloads delegate to closure overload with empty list
    public JsonElement ProjectInput(SchemaDescriptor? schema)
        => ProjectInput(schema, Array.Empty<SchemaDescriptor>());

    public JsonElement? ProjectOutput(SchemaDescriptor? schema)
        => schema is null ? null : ProjectOutput(schema, Array.Empty<SchemaDescriptor>());

    // Closure-aware overloads
    public JsonElement ProjectInput(SchemaDescriptor? schema, IReadOnlyList<SchemaDescriptor> referencedSchemas)
        => Project(schema, referencedSchemas);

    public JsonElement? ProjectOutput(SchemaDescriptor? schema, IReadOnlyList<SchemaDescriptor> referencedSchemas)
        => schema is null ? null : Project(schema, referencedSchemas);

    private static JsonElement Project(SchemaDescriptor? schema, IReadOnlyList<SchemaDescriptor> referencedSchemas)
    {
        try
        {
            return Projector.ProjectObject(schema, referencedSchemas);
        }
        catch (SchemaJsonContractException exception)
        {
            throw McpSchemaJsonContractExceptionMapper.Map(exception);
        }
    }
}

internal static class McpSchemaJsonContractExceptionMapper
{
    public static McpToolConfigurationException Map(SchemaJsonContractException exception)
        => exception.Violation switch
        {
            SchemaJsonContractViolation.ValidationRulesUnsupported =>
                new("MCP111", "Schema validation rules cannot be projected to MCP."),
            SchemaJsonContractViolation.ReferencesUnsupported =>
                new("MCP112", "Schema references cannot be projected to MCP."),
            SchemaJsonContractViolation.ScalarTypeUnsupported =>
                new("MCP113", "Schema field type is not supported by MCP projection."),
            SchemaJsonContractViolation.FieldTypeMissing =>
                new("MCP113", "Schema field type is missing."),
            SchemaJsonContractViolation.CollectionElementTypeMissing =>
                new("MCP113", "Collection element type is missing."),
            SchemaJsonContractViolation.RootContractNotObject =>
                new("MCP115", "MCP JSON contract root must be an object."),
            SchemaJsonContractViolation.PatternUnsupported =>
                new("MCP120", "Schema patterns are not portable to MCP JSON Schema."),
            SchemaJsonContractViolation.FieldIdentityInvalid =>
                new("MCP121", "Schema field identity is invalid."),
            SchemaJsonContractViolation.LengthConstraintInvalid =>
                new("MCP121", "Schema length constraint is invalid."),
            SchemaJsonContractViolation.NumericConstraintInvalid =>
                new("MCP121", "Schema numeric constraint is invalid."),
            SchemaJsonContractViolation.ConstraintNotApplicable =>
                new("MCP121", "Schema constraint is not applicable to its field type."),
            SchemaJsonContractViolation.IntegerConstraintInvalid =>
                new("MCP121", "Schema integer constraint is invalid."),
            SchemaJsonContractViolation.JsonPropertyMismatch =>
                new("MCP108", "Schema and JSON contract properties do not match."),
            SchemaJsonContractViolation.RequirednessMismatch =>
                new("MCP108", "Schema and JSON requiredness do not match."),
            SchemaJsonContractViolation.NullabilityMismatch =>
                new("MCP108", "Schema and JSON nullability do not match."),
            SchemaJsonContractViolation.PropertyTypeMismatch =>
                new("MCP108", "Schema and JSON property types do not match."),
            SchemaJsonContractViolation.AdditionalJsonProperty =>
                new("MCP108", "JSON contract contains a property not declared by the Schema."),
            _ => new("MCP121", "Schema JSON contract is invalid.")
        };
}

public sealed class McpToolConfigurationException : Exception
{
    public McpToolConfigurationException(string code, string message) : base(message) => Code = code;

    public string Code { get; }
}
