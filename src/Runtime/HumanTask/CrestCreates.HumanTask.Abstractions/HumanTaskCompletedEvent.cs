using CrestCreates.EventBus.Abstractions;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.State;

namespace CrestCreates.HumanTask.Abstractions;

public sealed class HumanTaskCompletedEvent : ILocalEvent
{
    public string EventId { get; init; } = string.Empty;
    public RuntimeInstanceKey HumanTaskKey { get; init; }
    public RuntimeInstanceKey? WorkflowKey { get; init; }
    public RuntimeDescriptorPin HumanTaskPin { get; init; } = default!;
    public string Outcome { get; init; } = string.Empty;
    public string? ActorId { get; init; }
    public string[] ActorRoles { get; init; } = [];
    public RuntimeStateValue? Result { get; init; }
}
