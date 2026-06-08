using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Schema;

public static class SchemaRegistryProvider
{
    private static ISchemaRegistry? _registry;

    public static void SetRegistry(ISchemaRegistry registry)
    {
        _registry = registry;
    }

    public static void Register(SchemaDescriptor descriptor)
    {
        if (_registry is SchemaRegistry concrete)
        {
            concrete.Register(descriptor);
        }
    }
}
