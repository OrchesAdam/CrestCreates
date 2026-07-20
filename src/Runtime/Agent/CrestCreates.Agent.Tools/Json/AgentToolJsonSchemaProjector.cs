using System.Text.Json;
using CrestCreates.Schema;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Agent.Tools;

public interface IAgentToolJsonSchemaProjector
{
    JsonElement ProjectInput(SchemaDescriptor? schema, IReadOnlyList<SchemaDescriptor>? referencedSchemas = null);

    JsonElement? ProjectOutput(SchemaDescriptor? schema, IReadOnlyList<SchemaDescriptor>? referencedSchemas = null);
}

public sealed class AgentToolJsonSchemaProjector : IAgentToolJsonSchemaProjector
{
    private readonly SchemaJsonContractProjector _projector;

    public AgentToolJsonSchemaProjector(SchemaJsonContractProjector projector)
        => _projector = projector;

    public JsonElement ProjectInput(SchemaDescriptor? schema, IReadOnlyList<SchemaDescriptor>? referencedSchemas = null) => Project(schema, referencedSchemas);

    public JsonElement? ProjectOutput(SchemaDescriptor? schema, IReadOnlyList<SchemaDescriptor>? referencedSchemas = null)
        => schema is null ? null : Project(schema, referencedSchemas);

    private JsonElement Project(SchemaDescriptor? schema, IReadOnlyList<SchemaDescriptor>? referencedSchemas)
    {
        try
        {
            return _projector.ProjectObject(schema, referencedSchemas ?? Array.Empty<SchemaDescriptor>());
        }
        catch (SchemaJsonContractException exception)
        {
            throw AgentToolSchemaJsonContractExceptionMapper.UnsupportedSchema(exception);
        }
    }
}
