using System.Text.Json.Serialization.Metadata;
using CrestCreates.Schema;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Agent.Tools;

public sealed class AgentToolSchemaParityValidator
{
    private readonly SchemaJsonTypeInfoParityValidator _validator;

    public AgentToolSchemaParityValidator(SchemaJsonTypeInfoParityValidator validator)
        => _validator = validator;

    public void ValidateInput(SchemaDescriptor schema, JsonTypeInfo typeInfo)
        => Validate(() => _validator.ValidateInput(schema, typeInfo));

    public void ValidateOutput(SchemaDescriptor schema, JsonTypeInfo typeInfo)
        => Validate(() => _validator.ValidateOutput(schema, typeInfo));

    private static void Validate(Action validation)
    {
        try
        {
            validation();
        }
        catch (SchemaJsonContractException exception)
        {
            throw exception.Violation == SchemaJsonContractViolation.RootContractNotObject
                ? new AgentToolConfigurationException(
                    AgentToolStartupDiagnosticCodes.JsonRootNotObject,
                    "Agent Tool JSON contract root must be an object.",
                    exception)
                : AgentToolSchemaJsonContractExceptionMapper.Parity(exception);
        }
    }
}

internal static class AgentToolSchemaJsonContractExceptionMapper
{
    public static AgentToolConfigurationException UnsupportedSchema(SchemaJsonContractException exception)
        => new(
            AgentToolStartupDiagnosticCodes.UnsupportedSchemaContract,
            "Schema cannot be represented by the supported Agent Tool JSON Schema subset.",
            exception);

    public static AgentToolConfigurationException Parity(SchemaJsonContractException exception)
        => new(
            AgentToolStartupDiagnosticCodes.SchemaJsonParityFailure,
            "Schema and application JSON contract are not directionally compatible.",
            exception);
}
