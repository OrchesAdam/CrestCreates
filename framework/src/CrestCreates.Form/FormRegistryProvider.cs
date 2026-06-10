using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Form.Abstractions;

namespace CrestCreates.Form;

public static class FormRegistryProvider
{
    private static readonly InMemoryFormProvider _provider = new();

    public static void SetRegistry(IFormRegistry registry)
    {
        DescriptorProviderRegistry.Register<FormDescriptor>(_provider);
    }

    public static void Register(FormDescriptor descriptor)
    {
        _provider.Add(descriptor);
    }

    private class InMemoryFormProvider : IDescriptorProvider<FormDescriptor>
    {
        private readonly List<FormDescriptor> _descriptors = new();

        public void Add(FormDescriptor descriptor) => _descriptors.Add(descriptor);
        public IReadOnlyList<FormDescriptor> GetDescriptors() => _descriptors;
    }
}
