using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.HumanTask.Abstractions;

namespace CrestCreates.HumanTask;

public static class HumanTaskRegistryProvider
{
    private static readonly InMemoryHumanTaskProvider _provider = new();

    public static void SetRegistry(IHumanTaskRegistry registry)
    {
        DescriptorProviderRegistry.Register<HumanTaskDescriptor>(_provider);
    }

    public static void Register(HumanTaskDescriptor descriptor)
    {
        _provider.Add(descriptor);
    }

    private class InMemoryHumanTaskProvider : IDescriptorProvider<HumanTaskDescriptor>
    {
        private readonly List<HumanTaskDescriptor> _descriptors = new();

        public void Add(HumanTaskDescriptor descriptor) => _descriptors.Add(descriptor);
        public IReadOnlyList<HumanTaskDescriptor> GetDescriptors() => _descriptors;
    }
}
