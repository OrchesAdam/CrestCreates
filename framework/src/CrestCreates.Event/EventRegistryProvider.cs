using CrestCreates.Event.Abstractions;

namespace CrestCreates.Event;

public static class EventRegistryProvider
{
    private static EventRegistry? _registry;

    public static void SetRegistry(EventRegistry registry)
    {
        _registry = registry;
    }

    public static void Register(EventDescriptor descriptor)
    {
        _registry?.Register(descriptor);
    }
}
