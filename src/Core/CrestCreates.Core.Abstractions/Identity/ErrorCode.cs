namespace CrestCreates.Core.Abstractions.Identity;

public readonly record struct ErrorCode
{
    public string? Value { get; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public ErrorCode(string value)
        => Value = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Error code cannot be empty.", nameof(value))
            : value;

    public string RequireValue()
        => string.IsNullOrWhiteSpace(Value)
            ? throw new InvalidOperationException("Error code is empty.")
            : Value;

    public override string ToString() => Value ?? string.Empty;

    public static implicit operator string(ErrorCode code) => code.Value ?? string.Empty;
}
