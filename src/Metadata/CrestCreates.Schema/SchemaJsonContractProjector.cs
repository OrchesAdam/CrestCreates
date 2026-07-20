using System.Text.Json;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Schema;

/// <summary>
/// Projects the framework's bounded portable Schema subset to canonical JSON
/// Schema. Nested references are resolved only from the explicit trusted
/// descriptor set supplied by the caller.
/// </summary>
public sealed class SchemaJsonContractProjector
{
    public JsonElement ProjectObject(SchemaDescriptor? schema)
        => ProjectObject(schema, Array.Empty<SchemaDescriptor>());

    public JsonElement ProjectObject(
        SchemaDescriptor? schema,
        IReadOnlyList<SchemaDescriptor> referencedSchemas)
        => new SchemaJsonNestedContractProjector().ProjectObject(schema, referencedSchemas);
}
