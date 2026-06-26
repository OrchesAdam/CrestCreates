namespace CrestCreates.Core.Abstractions.Identity;

public readonly record struct SeverityLevel
{
    private SeverityLevel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("Severity level is empty.");
        Value = value;
    }

    public string Value { get; init; } = null!;

    public bool IsEmpty => Value is null;

    public string RequireValue() => Value
        ?? throw new InvalidOperationException("Severity level is empty.");

    public override string ToString() => Value ?? string.Empty;

    public static implicit operator string(SeverityLevel level) => level.Value ?? string.Empty;

    public static SeverityLevel Error { get; } = new("Error");
    public static SeverityLevel Warning { get; } = new("Warning");
    public static SeverityLevel Info { get; } = new("Info");
}
