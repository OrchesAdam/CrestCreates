namespace CrestCreates.Core.Abstractions.Identity;

public readonly record struct DescriptorId
{
    public string? Value { get; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public DescriptorId(string value)
        => Value = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Descriptor id cannot be empty.", nameof(value))
            : value;

    public string RequireValue()
        => string.IsNullOrWhiteSpace(Value)
            ? throw new InvalidOperationException("Descriptor id is empty.")
            : Value;

    public override string ToString() => Value ?? string.Empty;

    public static implicit operator string(DescriptorId id) => id.Value ?? string.Empty;
}
