using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Schema;

public static class SchemaRegistryProvider
{
    private static readonly InMemorySchemaProvider _provider = new();

    public static void SetRegistry(ISchemaRegistry registry)
    {
        DescriptorProviderRegistry.Register<SchemaDescriptor>(_provider);
    }

    public static void Register(SchemaDescriptor descriptor)
    {
        _provider.Add(descriptor);
    }

    private class InMemorySchemaProvider : IDescriptorProvider<SchemaDescriptor>
    {
        private readonly List<SchemaDescriptor> _descriptors = new();

        public void Add(SchemaDescriptor descriptor) => _descriptors.Add(descriptor);
        public IReadOnlyList<SchemaDescriptor> GetDescriptors() => _descriptors;
    }
}
