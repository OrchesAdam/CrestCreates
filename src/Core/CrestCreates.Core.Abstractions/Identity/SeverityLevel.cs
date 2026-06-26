namespace CrestCreates.Core.Abstractions.Identity;

public readonly record struct SeverityLevel
{
    public string? Value { get; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    private SeverityLevel(string value)
        => Value = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Severity level cannot be empty.", nameof(value))
            : value;

    public string RequireValue()
        => string.IsNullOrWhiteSpace(Value)
            ? throw new InvalidOperationException("Severity level is empty.")
            : Value;

    public override string ToString() => Value ?? string.Empty;

    public static implicit operator string(SeverityLevel level)
        => level.Value ?? string.Empty;

    public static SeverityLevel Error { get; } = new("Error");
    public static SeverityLevel Warning { get; } = new("Warning");
    public static SeverityLevel Info { get; } = new("Info");
}
