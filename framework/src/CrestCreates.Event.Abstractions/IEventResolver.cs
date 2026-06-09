namespace CrestCreates.Event.Abstractions;

public interface IEventResolver
{
    IEventDescriptor? GetByName(string name);
    IEventDescriptor? GetByPayloadType(Type type);
}
