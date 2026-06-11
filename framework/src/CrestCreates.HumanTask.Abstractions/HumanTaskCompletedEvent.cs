namespace CrestCreates.HumanTask.Abstractions;

public sealed class HumanTaskCompletedEvent
{
    public string HumanTaskId { get; init; } = string.Empty;
    public string Outcome { get; init; } = string.Empty;
    public object? Result { get; init; }
}
