namespace CrestCreates.Core.Abstractions.Identity;

public readonly record struct CapabilityId
{
    public string? Value { get; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public CapabilityId(string value)
        => Value = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Capability id cannot be empty.", nameof(value))
            : value;

    public string RequireValue()
        => string.IsNullOrWhiteSpace(Value)
            ? throw new InvalidOperationException("Capability id is empty.")
            : Value;

    public override string ToString() => Value ?? string.Empty;

    public static implicit operator string(CapabilityId id) => id.Value ?? string.Empty;
}
