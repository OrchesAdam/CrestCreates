namespace CrestCreates.Core.Abstractions.Identity;

public readonly record struct PermissionName
{
    public string? Value { get; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public PermissionName(string value)
        => Value = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Permission name cannot be empty.", nameof(value))
            : value;

    public string RequireValue()
        => string.IsNullOrWhiteSpace(Value)
            ? throw new InvalidOperationException("Permission name is empty.")
            : Value;

    public override string ToString() => Value ?? string.Empty;

    public static implicit operator string(PermissionName name) => name.Value ?? string.Empty;
}
