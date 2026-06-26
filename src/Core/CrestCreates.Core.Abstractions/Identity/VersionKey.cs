namespace CrestCreates.Core.Abstractions.Identity;

public readonly record struct VersionKey
{
    public string? Value { get; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public VersionKey(string value)
        => Value = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Version key cannot be empty.", nameof(value))
            : value;

    public string RequireValue()
        => string.IsNullOrWhiteSpace(Value)
            ? throw new InvalidOperationException("Version key is empty.")
            : Value;

    public override string ToString() => Value ?? string.Empty;

    public static implicit operator string(VersionKey key) => key.Value ?? string.Empty;
}
