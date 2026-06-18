namespace CrestCreates.Event.Abstractions;

public interface IEventDescriptor
{
    string Id { get; }
    string Name { get; }
    EventScope Scope { get; }
    EventImportance Importance { get; }
    bool IsAuditable { get; }
    bool IsReplayable { get; }
    bool IsPublic { get; }
    string? Description { get; }
}
