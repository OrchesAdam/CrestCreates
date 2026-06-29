using System.Text.Json.Serialization;

namespace CrestCreates.Core.Abstractions.Identity;

[JsonConverter(typeof(SeverityLevelJsonConverter))]
public readonly record struct SeverityLevel : IComparable<SeverityLevel>
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

    private int Ordinal => Value switch
    {
        "Blocker" => 5,
        "Error" => 4,
        "Review" => 3,
        "Warning" => 2,
        "Info" => 1,
        _ => 0
    };

    public int CompareTo(SeverityLevel other) => Ordinal.CompareTo(other.Ordinal);

    public static SeverityLevel Error { get; } = new("Error");
    public static SeverityLevel Warning { get; } = new("Warning");
    public static SeverityLevel Info { get; } = new("Info");
    public static SeverityLevel Blocker { get; } = new("Blocker");
    public static SeverityLevel Review { get; } = new("Review");
}
