namespace CrestCreates.Core.Abstractions.Identity;

public readonly record struct EventName
{
    public string? Value { get; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public EventName(string value)
        => Value = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Event name cannot be empty.", nameof(value))
            : value;

    public string RequireValue()
        => string.IsNullOrWhiteSpace(Value)
            ? throw new InvalidOperationException("Event name is empty.")
            : Value;

    public override string ToString() => Value ?? string.Empty;

    public static implicit operator string(EventName name) => name.Value ?? string.Empty;
}
