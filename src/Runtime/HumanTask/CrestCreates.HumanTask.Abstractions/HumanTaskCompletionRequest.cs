using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.State;

namespace CrestCreates.HumanTask.Abstractions;

public sealed class HumanTaskCompletionRequest
{
    public RuntimeInstanceKey HumanTaskKey { get; init; }
    public string Outcome { get; init; } = default!;
    public RuntimeStateValue? Result { get; init; }
}
