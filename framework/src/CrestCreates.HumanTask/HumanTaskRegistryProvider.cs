using CrestCreates.HumanTask.Abstractions;

namespace CrestCreates.HumanTask;

public static class HumanTaskRegistryProvider
{
    private static HumanTaskRegistry? _registry;

    public static void SetRegistry(HumanTaskRegistry registry)
    {
        _registry = registry;
    }

    public static void Register(HumanTaskDescriptor descriptor)
    {
        _registry?.Register(descriptor);
    }
}
