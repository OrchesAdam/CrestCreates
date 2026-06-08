using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

public static class WorkflowRegistryProvider
{
    private static WorkflowRegistry? _registry;

    public static void SetRegistry(WorkflowRegistry registry)
    {
        _registry = registry;
    }

    public static void Register(WorkflowDescriptor descriptor)
    {
        _registry?.Register(descriptor);
    }
}
