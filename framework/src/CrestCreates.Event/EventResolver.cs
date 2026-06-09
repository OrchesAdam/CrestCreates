using CrestCreates.Event.Abstractions;

namespace CrestCreates.Event;

public sealed class EventResolver : IEventResolver
{
    private readonly IEventRegistry _generated;
    private readonly IDynamicEventRegistry _dynamic;

    public EventResolver(IEventRegistry generated, IDynamicEventRegistry dynamic)
    {
        _generated = generated;
        _dynamic = dynamic;
    }

    public IEventDescriptor? GetByName(string name)
        => (IEventDescriptor?)_generated.GetByName(name) ?? _dynamic.GetByName(name);

    public IEventDescriptor? GetByPayloadType(Type type)
        => _generated.GetByPayloadType(type);
}
