using CrestCreates.Form.Abstractions;

namespace CrestCreates.Form;

public static class FormRegistryProvider
{
    private static FormRegistry? _registry;

    public static void SetRegistry(FormRegistry registry)
    {
        _registry = registry;
    }

    public static void Register(FormDescriptor descriptor)
    {
        _registry?.Register(descriptor);
    }
}
