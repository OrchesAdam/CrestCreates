using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Schema.Abstractions;

public interface ISchemaDescriptorProvider
{
    SchemaDescriptor GetSchemaDescriptor();
}
