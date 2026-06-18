using System.Collections.Concurrent;
using CrestCreates.Event.Abstractions;
using RegistryState = CrestCreates.Metadata.Abstractions.RegistryState;

namespace CrestCreates.Event;

public sealed class DynamicEventRegistry : IDynamicEventRegistry
{
    private readonly ConcurrentDictionary<string, DynamicEventDescriptor> _byName = new();
    private readonly IEventRegistry _generated;

    public DynamicEventRegistry(IEventRegistry generated) => _generated = generated;

    public bool TryRegister(string name, Type? payloadType, EventScope scope)
    {
        AssertScopeLocal(scope);
        AssertBuilt();
        if (_generated.GetByName(name) is not null) return false;
        return _byName.TryAdd(name, new DynamicEventDescriptor
        {
            Id = DynamicEventDescriptor.GenerateId(name),
            Name = name,
            PayloadType = payloadType,
            Scope = scope
        });
    }

    public void Upsert(string name, Type? payloadType, EventScope scope)
    {
        AssertScopeLocal(scope);
        AssertBuilt();
        if (_generated.GetByName(name) is not null)
            throw new InvalidOperationException(
                $"Dynamic event '{name}' conflicts with an existing generated event. " +
                "Dynamic events cannot shadow generated events. " +
                "Use a different name or register the event via [CrestEvent].");
        _byName[name] = new DynamicEventDescriptor
        {
            Id = DynamicEventDescriptor.GenerateId(name),
            Name = name,
            PayloadType = payloadType,
            Scope = scope
        };
    }

    public DynamicEventDescriptor? GetByName(string name)
        => _byName.TryGetValue(name, out var d) ? d : null;

    private static void AssertScopeLocal(EventScope scope)
    {
        if (scope != EventScope.Local)
            throw new ArgumentException(
                $"Dynamic events are restricted to Scope.Local. " +
                $"Requested: {scope}. Use [CrestEvent] for Domain/Integration events.");
    }

    private void AssertBuilt()
    {
        if (_generated.State != RegistryState.Built)
            throw new InvalidOperationException(
                "Cannot register dynamic events before EventRegistry.Build() completes.");
    }
}
