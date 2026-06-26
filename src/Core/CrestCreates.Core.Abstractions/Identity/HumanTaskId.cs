namespace CrestCreates.Core.Abstractions.Identity;

public readonly record struct HumanTaskId
{
    public string? Value { get; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public HumanTaskId(string value)
        => Value = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Human task id cannot be empty.", nameof(value))
            : value;

    public string RequireValue()
        => string.IsNullOrWhiteSpace(Value)
            ? throw new InvalidOperationException("Human task id is empty.")
            : Value;

    public override string ToString() => Value ?? string.Empty;

    public static implicit operator string(HumanTaskId id) => id.Value ?? string.Empty;
}
