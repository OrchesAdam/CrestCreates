using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.State;

namespace CrestCreates.HumanTask.Abstractions;

public sealed class HumanTaskCompletionRequest
{
    public RuntimeInstanceKey HumanTaskKey { get; init; }
    public string Outcome { get; init; } = default!;
    public string? ActorId { get; init; }
    public string[] ActorRoles { get; init; } = [];
    public RuntimeStateValue? Result { get; init; }
}
