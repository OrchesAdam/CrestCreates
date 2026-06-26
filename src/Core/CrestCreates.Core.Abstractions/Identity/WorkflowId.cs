namespace CrestCreates.Core.Abstractions.Identity;

public readonly record struct WorkflowId
{
    public string? Value { get; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public WorkflowId(string value)
        => Value = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Workflow id cannot be empty.", nameof(value))
            : value;

    public string RequireValue()
        => string.IsNullOrWhiteSpace(Value)
            ? throw new InvalidOperationException("Workflow id is empty.")
            : Value;

    public override string ToString() => Value ?? string.Empty;

    public static implicit operator string(WorkflowId id) => id.Value ?? string.Empty;
}
