using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Event.Abstractions;

public interface IEventRegistry : IVersionedDescriptorRegistry<EventDescriptor>
{
    IReadOnlyList<EventDescriptor> GetByCategory(EventCategory category);
    IReadOnlyList<EventDescriptor> GetBySemantic(EventSemantic semantic);
    IReadOnlyList<EventDescriptor> GetByImportance(EventImportance importance);
}
