using CrestCreates.Metadata.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Runtime.Persistence.State;

public sealed class RuntimeStateContractStartupValidator : ISchemaRefValidator
{
    private readonly ISchemaRegistry _schemaRegistry;

    public RuntimeStateContractStartupValidator(ISchemaRegistry schemaRegistry)
        => _schemaRegistry = schemaRegistry ?? throw new ArgumentNullException(nameof(schemaRegistry));

    public void Validate(DescriptorRef? schemaRef)
    {
        if (schemaRef is null)
            return;

        var reference = schemaRef.Value;
        if (!string.Equals(reference.Namespace, "schema", StringComparison.Ordinal)
            || reference.Version is not > 0)
        {
            throw new RuntimeStateContractException(
                "Runtime state SchemaRef must be an exact positive schema reference.");
        }

        var schema = _schemaRegistry.GetByVersion(reference.Id, reference.Version.Value);
        if (schema is null
            || !string.Equals(schema.Namespace, reference.Namespace, StringComparison.Ordinal)
            || !string.Equals(schema.Id, reference.Id, StringComparison.Ordinal)
            || schema.Version != reference.Version.Value
            || schema.Kind != DescriptorKind.Schema)
        {
            throw new RuntimeStateContractException(
                $"Runtime state SchemaRef '{reference.FullId}' v{reference.Version.Value} cannot be resolved exactly.");
        }
    }
}
