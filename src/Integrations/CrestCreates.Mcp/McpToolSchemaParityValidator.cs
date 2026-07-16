using System.Text.Json.Serialization.Metadata;
using CrestCreates.Schema;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Mcp;

public sealed class McpToolSchemaParityValidator
{
    private static readonly SchemaJsonTypeInfoParityValidator Validator = new();

    public void ValidateInput(SchemaDescriptor schema, JsonTypeInfo typeInfo)
        => Validate(() => Validator.ValidateInput(schema, typeInfo));

    public void ValidateOutput(SchemaDescriptor schema, JsonTypeInfo typeInfo)
        => Validate(() => Validator.ValidateOutput(schema, typeInfo));

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
