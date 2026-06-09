namespace CrestCreates.Event.Abstractions;

public interface IEventDescriptorProvider
{
    IReadOnlyList<GeneratedEventDescriptor> GetDescriptors();
}
