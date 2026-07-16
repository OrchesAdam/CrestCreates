using System.Text.Json;
using CrestCreates.Schema;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Agent.Tools;

public interface IAgentToolJsonSchemaProjector
{
    JsonElement ProjectInput(SchemaDescriptor? schema);

    JsonElement? ProjectOutput(SchemaDescriptor? schema);
}

public sealed class AgentToolJsonSchemaProjector : IAgentToolJsonSchemaProjector
{
    private readonly SchemaJsonContractProjector _projector;

    public AgentToolJsonSchemaProjector(SchemaJsonContractProjector projector)
        => _projector = projector;

    public JsonElement ProjectInput(SchemaDescriptor? schema) => Project(schema);

    public JsonElement? ProjectOutput(SchemaDescriptor? schema)
        => schema is null ? null : Project(schema);

    private JsonElement Project(SchemaDescriptor? schema)
    {
        try
        {
            return _projector.ProjectObject(schema);
        }
        catch (SchemaJsonContractException exception)
        {
            throw AgentToolSchemaJsonContractExceptionMapper.UnsupportedSchema(exception);
        }
    }
}
