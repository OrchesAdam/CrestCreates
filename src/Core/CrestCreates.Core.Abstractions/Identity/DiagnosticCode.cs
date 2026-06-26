namespace CrestCreates.Core.Abstractions.Identity;

public readonly record struct DiagnosticCode
{
    public string? Value { get; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public DiagnosticCode(string value)
        => Value = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Diagnostic code cannot be empty.", nameof(value))
            : value;

    public string RequireValue()
        => string.IsNullOrWhiteSpace(Value)
            ? throw new InvalidOperationException("Diagnostic code is empty.")
            : Value;

    public override string ToString() => Value ?? string.Empty;

    public static implicit operator string(DiagnosticCode code) => code.Value ?? string.Empty;
}
