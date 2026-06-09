namespace CrestCreates.Event.Abstractions;

public interface IDynamicEventRegistry
{
    bool TryRegister(string name, Type? payloadType, EventScope scope);
    void Upsert(string name, Type? payloadType, EventScope scope);
    DynamicEventDescriptor? GetByName(string name);
}
