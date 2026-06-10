using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

public static class WorkflowRegistryProvider
{
    private static readonly InMemoryWorkflowProvider _provider = new();

    public static void SetRegistry(IWorkflowRegistry registry)
    {
        DescriptorProviderRegistry.Register<WorkflowDescriptor>(_provider);
    }

    public static void Register(WorkflowDescriptor descriptor)
    {
        _provider.Add(descriptor);
    }

    private class InMemoryWorkflowProvider : IDescriptorProvider<WorkflowDescriptor>
    {
        private readonly List<WorkflowDescriptor> _descriptors = new();

        public void Add(WorkflowDescriptor descriptor) => _descriptors.Add(descriptor);
        public IReadOnlyList<WorkflowDescriptor> GetDescriptors() => _descriptors;
    }
}
