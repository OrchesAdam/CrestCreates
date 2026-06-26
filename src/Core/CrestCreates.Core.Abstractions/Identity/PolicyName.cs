namespace CrestCreates.Core.Abstractions.Identity;

public readonly record struct PolicyName
{
    public string? Value { get; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public PolicyName(string value)
        => Value = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Policy name cannot be empty.", nameof(value))
            : value;

    public string RequireValue()
        => string.IsNullOrWhiteSpace(Value)
            ? throw new InvalidOperationException("Policy name is empty.")
            : Value;

    public override string ToString() => Value ?? string.Empty;

    public static implicit operator string(PolicyName name) => name.Value ?? string.Empty;
}
