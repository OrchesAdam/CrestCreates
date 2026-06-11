namespace CrestCreates.HumanTask.Abstractions;

public sealed class HumanTaskCompletionRequest
{
    public string HumanTaskInstanceId { get; init; } = default!;
    public string Outcome { get; init; } = default!;
    public object? Result { get; init; }
}
